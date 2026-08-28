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

    private static readonly uint DefaultAbilityHash = Hash("Default");

    public uint AvatarId { get; private init; }
    public ulong Guid { get; private init; }
    public uint BornTime { get; private init; }

    public uint SkillDepotId { get; private init; }
    public IReadOnlyList<uint> Skills { get; private init; } = [];
    public IReadOnlyList<string> Abilities { get; private init; } = [];

    public uint WeaponItemId { get; private init; }
    public uint WeaponGadgetId { get; private init; }
    public ulong WeaponGuid { get; private init; }

    public IReadOnlyDictionary<uint, float> FightProps { get; private init; } = new Dictionary<uint, float>();

    /// <summary>
    /// Builds avatar <paramref name="avatarId"/> from excel, carrying its default weapon.<br/>
    /// The weapon takes the guid straight after <paramref name="guid"/>, which is the order the
    /// client expects the pair to arrive in.
    /// </summary>
    public static Avatar Create(GameData data, uint avatarId, ulong guid)
    {
        var config = data.AvatarData[avatarId];
        var depot = data.AvatarSkillDepotData[config.SkillDepotId];
        var weapon = data.WeaponData[config.InitialWeapon];

        return new Avatar {
            AvatarId = avatarId,
            Guid = guid,
            SkillDepotId = config.SkillDepotId,
            BornTime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            WeaponItemId = config.InitialWeapon,
            WeaponGadgetId = weapon.GadgetId,
            WeaponGuid = guid + 1,
            // Depots pad their skill list with zeroes for the slots a character doesn't have.
            Skills = [.. depot.Skills.Append(depot.EnergySkill).Where(skill => skill != 0)],
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
            },
            Abilities = [.. data.Avatars[avatarId].AbilityNames]
        };
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
            PropMap = {
                [(uint)PlayerProperty.Exp] = PlayerProperty.Exp.Value(0),
                [(uint)PlayerProperty.Level] = PlayerProperty.Level.Value(1)
            }
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

    /// <summary>Binds this avatar's abilities to the embryo slots the client invokes them through.</summary>
    public AbilityControlBlock ControlBlock()
    {
        var block = new AbilityControlBlock();

        foreach (var (index, name) in Abilities.Index())
        {
            // Embryos are numbered from one, and the client quotes that number back at us in
            // every invocation, so this has to follow declaration order exactly.
            block.AbilityEmbryoList.Add(new AbilityEmbryo {
                AbilityId = (uint)index + 1,
                AbilityNameHash = Hash(name),
                AbilityOverrideNameHash = DefaultAbilityHash
            });
        }

        return block;
    }

    /// <summary>The 131-multiplier string hash the client looks abilities up by. The overflow is part of it.</summary>
    private static uint Hash(string name)
        => name.Aggregate(seed: 0u, (hash, character) => hash * 131 + character);
}
