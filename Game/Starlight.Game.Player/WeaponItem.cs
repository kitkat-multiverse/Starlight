using Starlight.Protocol;

namespace Starlight.Game.Player;

public sealed class WeaponItem : InventoryItem
{
    public uint GadgetId { get; init; }
    public uint Level { get; init; } = 1;
    public uint Refinement { get; init; } = 1;
    public uint PromoteLevel { get; init; }
    public uint AffixId { get; init; }

    public override Item ToProtocol()
    {
        var weapon = new Weapon {
            Level = Level,
            PromoteLevel = PromoteLevel
        };

        if (AffixId != 0)
            weapon.AffixMap[AffixId] = Refinement - 1;

        return new Item {
            ItemId = ItemId,
            Guid = Guid,
            Equip = new Equip { Weapon = weapon }
        };
    }

    public SceneWeaponInfo ToSceneProtocol()
    {
        var weapon = new SceneWeaponInfo {
            ItemId = ItemId,
            Guid = Guid,
            GadgetId = GadgetId,
            Level = Level,
            PromoteLevel = PromoteLevel,
            AbilityInfo = new AbilitySyncStateInfo { IsInited = AffixId != 0 }
        };

        if (AffixId != 0)
            weapon.AffixMap[AffixId] = Refinement - 1;

        return weapon;
    }

    public static uint PromoteLevelFor(uint level) => level switch {
        > 80 => 6,
        > 70 => 5,
        > 60 => 4,
        > 50 => 3,
        > 40 => 2,
        > 20 => 1,
        _ => 0
    };
}
