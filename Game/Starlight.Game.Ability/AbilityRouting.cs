using Starlight.Game.Player;
using Starlight.Protobuf.Core;
using Starlight.Protocol;

namespace Starlight.Game.Ability;

public readonly record struct AbilityScopeContext(
    AbilityScope Scope,
    uint PeerId,
    uint HostPeerId,
    uint SceneId
)
{
    public bool TryGet(uint entityId, out AbilityComponent component) =>
        Scope.TryGet(SceneId, entityId, out component);
}

public interface IAbilityScopeResolver
{
    bool TryResolve(IPlayer player, out AbilityScopeContext context);
}

public interface IAbilityForwarder
{
    Task Forward(IPlayer sender, ForwardType type, IMessage message, uint forwardPeer);
}
