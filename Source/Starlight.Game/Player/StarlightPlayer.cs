using Starlight.Game.Modules;
using Starlight.Protocol;
using Starlight.Rpc;
using Starlight.Rpc.Tunnel;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Game.Player;

public sealed class StarlightPlayer : IPlayer
{
    private readonly ModuleRegistry _registry;
    private readonly RpcTunnel _tunnel;
    private readonly IModule[] _modules;

    public StarlightPlayer(IServiceProvider provider, ModuleRegistry registry, RpcTunnel tunnel)
    {
        _registry = registry;
        _tunnel = tunnel;
        _modules = registry.CreateModules(provider, this);
    }

    public uint Uid { get; set; }

    public TModule Module<TModule>() where TModule : class, IModule
        => (TModule)_modules[_registry.IndexOf<TModule>()];

    public void Send(IMessage message)
        => _ = _tunnel.Publish(GameSubjects.OutboundPacket, message);

    /// <summary>Routes an inbound message to this player's handler modules.</summary>
    internal async ValueTask Dispatch(IMessage message)
    {
        try
        {
            await _registry.Dispatch(this, _modules, message);
        }
        catch (KickException kick)
        {
            // A handler aborted the chain: send any farewell packets, then ask the gate to
            // drop the client. Ordering is preserved across the tunnel, so a flush-disconnect
            // sees the replies queued ahead of it.
            foreach (var reply in kick.Replies)
            {
                Send(reply);
            }

            _ = _tunnel.Publish(GameSubjects.Disconnect, new DisconnectNotify {
                Reason = kick.Reason,
                Flush = kick.Flush
            });
        }
    }
}
