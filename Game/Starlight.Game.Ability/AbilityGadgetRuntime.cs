using Starlight.Game.Player;
using Starlight.Protocol;

namespace Starlight.Game.Ability;

public readonly record struct AbilityGadgetCreateRequest(
    uint GadgetId,
    Vector Position,
    Vector Rotation,
    uint CampId,
    uint CampType,
    uint OwnerEntityId,
    uint TargetEntityId,
    bool SightGroupWithOwner,
    bool AliveByOwner
);

public readonly record struct AbilityElemBallRequest(
    uint ItemId,
    uint GadgetId,
    uint Count,
    Vector Position,
    Vector Rotation
);

public interface IAbilityGadgetRuntime
{
    ValueTask CreateGadgetAsync(IPlayer player, AbilityGadgetCreateRequest request);
    ValueTask GenerateElemBallsAsync(IPlayer player, AbilityElemBallRequest request);
    ValueTask KillGadgetsAsync(IPlayer player, uint gadgetId);
}
