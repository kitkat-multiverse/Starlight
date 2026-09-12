using Starlight.Game.Resources;
using Starlight.Game.Resources.Excel;
using Starlight.Protocol;

namespace Starlight.Game.World;

public sealed class MonsterEntity : SceneEntity
{
    private MonsterEntity(
        Scene scene,
        SceneEntityInfo info,
        MonsterData data,
        FightPropertyStore fightProperties,
        uint level,
        uint weaponEntityId,
        uint weaponGadgetId
    ) : base(scene, info, fightProperties)
    {
        Data = data;
        Level = level;
        WeaponEntityId = weaponEntityId;
        WeaponGadgetId = weaponGadgetId;
    }

    public MonsterData Data { get; }
    public IReadOnlyList<uint> Affixes => Data.Affixes;
    public IReadOnlyList<uint> Equips => Data.Equips;
    public uint Level { get; }
    public uint WeaponEntityId { get; }
    public uint WeaponGadgetId { get; }
    public override uint AuthorityPeerId => Info.Monster?.AuthorityPeerId ?? 0;

    public static MonsterEntity Create(
        Scene scene,
        GameData gameData,
        MonsterData monster,
        uint level,
        Vector position,
        Vector? rotation = null
    )
    {
        var world = scene.World;
        var fightProps = monster.CalculateFightProperties(level, gameData.MonsterCurveData);
        var fightProperties = new FightPropertyStore(fightProps);
        var weaponGadgetId = gameData.ResolveMonsterWeaponId(monster.Id);
        var weaponEntityId = weaponGadgetId == 0 ? 0 : world.NextEntityId(ProtEntityType.PROT_ENTITY_TYPE_WEAPON);

        var monsterInfo = new SceneMonsterInfo {
            MonsterId = monster.Id,
            AuthorityPeerId = world.HostPeerId,
            AffixList = [.. monster.Affixes],
            BornType = MonsterBornType.MONSTER_BORN_TYPE_DEFAULT,
            BlockId = scene.Id,
            KillNum = 1,
            SummonTagMap = gameData.ResolveMonsterSummonTags(monster.Id)
                .ToDictionary(tag => tag, _ => 0u)
        };

        if (string.Equals(monster.Type, "MONSTER_BOSS", StringComparison.Ordinal))
        {
            var describe = gameData.ResolveMonsterDescribe(monster.Id);

            if (describe is not null)
                monsterInfo.TitleId = describe.TitleId;

            monsterInfo.SpecialNameId = gameData.ResolveMonsterSpecialNameId(monster.Id);
        }

        if (weaponGadgetId != 0)
        {
            monsterInfo.WeaponList.Add(new SceneWeaponInfo {
                EntityId = weaponEntityId,
                GadgetId = weaponGadgetId,
                AbilityInfo = new AbilitySyncStateInfo()
            });
        }

        var info = new SceneEntityInfo {
            EntityType = ProtEntityType.PROT_ENTITY_TYPE_MONSTER,
            EntityId = world.NextEntityId(ProtEntityType.PROT_ENTITY_TYPE_MONSTER),
            LifeState = 1,
            MotionInfo = new MotionInfo {
                Pos = CopyVector(position),
                Rot = CopyVector(rotation),
                Speed = new Vector(),
                RefPos = new Vector(),
                State = MotionState.MOTION_STATE_STANDBY
            },
            EntityClientData = new EntityClientData(),
            EntityAuthorityInfo = new EntityAuthorityInfo {
                AbilityInfo = new AbilitySyncStateInfo(),
                AiInfo = new SceneEntityAiInfo {
                    IsAiOpen = true,
                    BornPos = CopyVector(position)
                },
                BornPos = CopyVector(position),
                ClientExtraInfo = new EntityClientExtraInfo { SkillAnchorPosition = new Vector() }
            },
            PropList = [
                new PropPair { Type = (uint)PlayerProperty.Level, PropValue = PlayerProperty.Level.Value(level) }
            ],
            Monster = monsterInfo
        };

        return new MonsterEntity(
            scene,
            info,
            monster,
            fightProperties,
            level,
            weaponEntityId,
            weaponGadgetId);
    }

    public void SyncAiSkillCooldowns(AiSkillCdInfo info)
    {
        var ai = Info.EntityAuthorityInfo?.AiInfo;

        if (ai is null)
            return;

        ai.SkillCdMap.Clear();
        ai.SkillGroupCdMap.Clear();

        foreach (var (skill, cd) in info.SkillCdMap)
        {
            ai.SkillCdMap[skill] = cd;
        }

        foreach (var (group, cd) in info.SkillGroupCdMap)
        {
            ai.SkillGroupCdMap[group] = cd;
        }
    }

    public void SyncAiThreat(AiThreatInfo info)
    {
        var ai = Info.EntityAuthorityInfo?.AiInfo;

        if (ai is null)
            return;

        ai.AiThreatMap.Clear();

        foreach (var (entityId, threat) in info.AiThreatMap)
        {
            ai.AiThreatMap[entityId] = threat;
        }
    }

    public void SetAlert(bool alert)
    {
        if (Info.EntityAuthorityInfo?.AiInfo is {} ai)
            ai.IsEnteredCombat = alert;
    }

    private static Vector CopyVector(Vector? source) =>
        source is null ? new Vector() : new Vector { X = source.X, Y = source.Y, Z = source.Z };
}
