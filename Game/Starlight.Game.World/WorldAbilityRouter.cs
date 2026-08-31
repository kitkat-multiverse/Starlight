using Starlight.Game.Ability;
using Starlight.Game.Player;
using Starlight.Protobuf.Core;
using Starlight.Protocol;

namespace Starlight.Game.World;

public sealed class WorldAbilityRouter : IAbilityScopeResolver, IAbilityForwarder
{
    public bool TryResolve(IPlayer player, out AbilityScopeContext context)
    {
        var module = player.Module<WorldModule>();
        var world = module.World;

        context = new AbilityScopeContext(
            world.Abilities,
            module.PeerId,
            world.HostPeerId,
            module.Scene?.Id ?? 0);
        return true;
    }

    public async Task Forward(IPlayer sender, ForwardType type, IMessage message, uint forwardPeer)
    {
        var senderModule = sender.Module<WorldModule>();
        var world = senderModule.World;

        if (type is ForwardType.FORWARD_TYPE_LOCAL or ForwardType.FORWARD_TYPE_ONLY_SERVER)
            return;

        var sceneId = senderModule.Scene?.Id ?? 0;

        var peers = world.Peers
            .Where(pair => pair.Value.Module<WorldModule>().Scene?.Id == sceneId);

        peers = type switch {
            ForwardType.FORWARD_TYPE_TO_ALL => peers,
            ForwardType.FORWARD_TYPE_TO_ALL_EXCEPT_CUR => peers.Where(pair => pair.Value != sender),
            ForwardType.FORWARD_TYPE_TO_ALL_EXIST_EXCEPT_CUR => peers.Where(pair => pair.Value != sender),
            ForwardType.FORWARD_TYPE_TO_HOST => peers.Where(pair => pair.Key == world.HostPeerId),
            ForwardType.FORWARD_TYPE_TO_ALL_GUEST => peers.Where(pair => pair.Key != world.HostPeerId),
            ForwardType.FORWARD_TYPE_TO_PEER => peers.Where(pair => pair.Key == forwardPeer),
            ForwardType.FORWARD_TYPE_TO_PEERS => peers.Where(pair => pair.Key == forwardPeer),
            _ => []
        };

        foreach (var (_, recipient) in peers)
        {
            await recipient.Send(message);
        }
    }
}
