using Starlight.Game.Player;

namespace Starlight.Game.Ability;

public readonly record struct AbilityDamageRequest(
    uint SourceEntityId,
    uint TargetEntityId,
    float Amount
);

public interface IAbilityDamageRuntime
{
    ValueTask ApplyLoseHpAsync(IPlayer player, AbilityDamageRequest request);
}
