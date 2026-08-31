using Starlight.Game.Resources;
using Starlight.Protocol;

namespace Starlight.Game.Player;

public sealed class Avatar
{
    public const uint DefaultFlycloak = 140001;

    private const uint Alive = 1;
    private const uint AvatarTypeFormal = 1;

    // Fight props the client wants before it will treat an avatar as alive.
    private const uint BaseHp = 1, BaseAttack = 4, BaseDefense = 7, Critical = 20, CriticalHurt = 22;
    private const uint CurHp = 1010, MaxHp = 2000, CurAttack = 2001, CurDefense = 2002;

    public uint AvatarId { get; private init; }
    public ulong Guid { get; private init; }
    public uint BornTime { get; private init; }

    public uint SkillDepotId { get; private init; }
    public IReadOnlyList<uint> Skills { get; private init; } = [];
    public IReadOnlyList<uint> Talents { get; private init; } = [];

    public uint Level { get; private init; } = 1;
    public uint PromoteLevel { get; private init; }
    public uint Constellation { get; private init; }

    public uint WeaponItemId { get; private set; }
    public uint WeaponGadgetId { get; private set; }
    public ulong WeaponGuid { get; private set; }

    public IReadOnlyDictionary<uint, float> FightProps { get; private init; } = new Dictionary<uint, float>();

    /// <summary>
    /// Builds avatar <paramref name="avatarId"/> from excel, carrying its default weapon.<br/>
    /// The weapon takes the guid straight after <paramref name="guid"/>, which is the order the
    /// client expects the pair to arrive in.
    /// </summary>
    public static Avatar Create(
        GameData data,
        uint avatarId,
        ulong guid,
        uint level = 1,
        uint constellation = 0,
        uint bornTime = 0,
        ulong weaponGuid = 0
    )
    {
        var config = data.AvatarData[avatarId];
        var depot = data.AvatarSkillDepotData[config.SkillDepotId];
        var weapon = data.WeaponData[config.InitialWeapon];

        level = Math.Clamp(level, min: 1u, max: 90u);
        constellation = Math.Clamp(constellation, min: 0u, max: 6u);

        return new Avatar {
            AvatarId = avatarId,
            Guid = guid,
            SkillDepotId = config.SkillDepotId,
            BornTime = bornTime == 0 ? (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds() : bornTime,
            WeaponItemId = config.InitialWeapon,
            WeaponGadgetId = weapon.GadgetId,
            WeaponGuid = weaponGuid == 0 ? guid + 1 : weaponGuid,
            Level = level,
            PromoteLevel = WeaponItem.PromoteLevelFor(level),
            Constellation = constellation,
            // Depots pad their skill list with zeroes for the slots a character doesn't have.
            Skills = [.. depot.Skills.Append(depot.EnergySkill).Where(skill => skill != 0)],
            Talents = [.. depot.Talents.Where(talent => talent != 0).Take((int)constellation)],
            FightProps = new Dictionary<uint, float> {
                [BaseHp] = config.HpBase,
                [BaseAttack] = config.AttackBase,
                [BaseDefense] = config.DefenseBase,
                [Critical] = config.CritChanceBase,
                [CriticalHurt] = config.CritDamageBase,
                [MaxHp] = config.HpBase,
                [CurHp] = config.HpBase,
                [CurAttack] = config.AttackBase,
                [CurDefense] = config.DefenseBase
            }
        };
    }

    /// <summary>Updates the weapon represented in this avatar's roster and scene data.</summary>
    internal void EquipWeapon(WeaponItem weapon)
    {
        WeaponItemId = weapon.ItemId;
        WeaponGadgetId = weapon.GadgetId;
        WeaponGuid = weapon.Guid;
    }

    /// <summary>This avatar as the client's roster sees it, rather than as a scene entity.</summary>
    public AvatarInfo Info()
    {
        var info = new AvatarInfo {
            AvatarType = AvatarTypeFormal,
            AvatarId = AvatarId,
            Guid = Guid,
            LifeState = Alive,
            SkillDepotId = SkillDepotId,
            BornTime = BornTime,
            WearingFlycloakId = DefaultFlycloak,
            EquipGuidList = [WeaponGuid],
            CoreProudSkillLevel = Constellation,
            FetterInfo = new AvatarFetterInfo { ExpLevel = 1 },
            PropMap = {
                [(uint)PlayerProperty.Exp] = PlayerProperty.Exp.Value(0),
                [(uint)PlayerProperty.Level] = PlayerProperty.Level.Value(Level),
                [(uint)PlayerProperty.BreakLevel] = PlayerProperty.BreakLevel.Value(PromoteLevel),
                [(uint)PlayerProperty.SatiationVal] = PlayerProperty.SatiationVal.Value(0),
                [(uint)PlayerProperty.SatiationPenaltyTime] =
                    PlayerProperty.SatiationPenaltyTime.Value(0)
            },
            TalentIdList = [.. Talents]
        };

        foreach (var (prop, value) in FightProps)
        {
            info.FightPropMap[prop] = value;
        }

        foreach (var skill in Skills)
        {
            info.SkillLevelMap[skill] = 1;
        }

        return info;
    }
}
