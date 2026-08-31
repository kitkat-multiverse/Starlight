using Starlight.Game.Player;
using Starlight.Game.Resources.Binary;
using Starlight.Protocol;

namespace Starlight.Game.Ability;

public sealed record AbilityContext(
    IPlayer Player,
    AbilityScopeContext World,
    AbilityInvokeEntry Invoke,
    AbilityComponent Source,
    AbilityComponent? Target,
    AbilityInstance? Ability,
    AbilityModifierInstance? Modifier,
    AbilityConfig? Definition,
    AbilityConfigNode? Action,
    AbilityConfigNode? Mixin
);
