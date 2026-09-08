using Starlight.Game.Player;
using Starlight.Protobuf.Core;
using Starlight.Protocol;

namespace Starlight.Game.Ability;

public readonly record struct AbilityScopeContext(
    AbilityScope Scope,
    uint PeerId,
    uint HostPeerId,
    uint SceneId,
    uint CurrentAvatarEntityId = 0
)
{
    public bool TryGet(uint entityId, out AbilityComponent component) =>
        Scope.TryGet(SceneId, entityId, out component);

    public bool TryGetCurrentAvatar(out AbilityComponent component)
    {
        component = null!;
        return CurrentAvatarEntityId != 0 && TryGet(CurrentAvatarEntityId, out component);
    }

    public bool TryGetOwner(AbilityComponent component, out AbilityComponent owner)
    {
        owner = null!;
        return component.Owner.OwnerEntityId != 0 && TryGet(component.Owner.OwnerEntityId, out owner);
    }

    public bool TryGetPropOwner(AbilityComponent component, out AbilityComponent owner)
    {
        owner = null!;
        var ownerEntityId = component.Owner.PropOwnerEntityId != 0 ? component.Owner.PropOwnerEntityId : component.Owner.OwnerEntityId;
        return ownerEntityId != 0 && TryGet(ownerEntityId, out owner);
    }

    public bool TryGetOriginOwner(AbilityComponent component, out AbilityComponent owner)
    {
        owner = component;
        var visited = new HashSet<uint>();

        while (owner.Owner.OwnerEntityId != 0)
        {
            if (!visited.Add(owner.Owner.EntityId) || !TryGet(owner.Owner.OwnerEntityId, out var next))
            {
                owner = null!;
                return false;
            }

            owner = next;
        }

        return true;
    }
}

public interface IAbilityScopeResolver
{
    bool TryResolve(IPlayer player, out AbilityScopeContext context);
}

public interface IInvokeForwarder
{
    Task Forward(IPlayer sender, ForwardType type, IMessage message, uint forwardPeer);
}
