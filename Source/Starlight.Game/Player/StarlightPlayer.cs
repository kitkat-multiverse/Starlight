using Starlight.Game.Modules;
using Starlight.Rpc;
using Starlight.Rpc.Tunnel;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Game.Player;

public sealed class StarlightPlayer : IPlayer
{
    private readonly ModuleRegistry _registry;
    private readonly RpcTunnel _tunnel;
    private readonly IModule[] _modules;

    public StarlightPlayer(ModuleRegistry registry, RpcTunnel tunnel)
    {
        _registry = registry;
        _tunnel = tunnel;
        _modules = registry.CreateModules(this);
    }

    public TModule Module<TModule>() where TModule : class, IModule
        => (TModule)_modules[_registry.IndexOf<TModule>()];

    public void Send(IMessage message)
        => _ = _tunnel.Publish(GameSubjects.OutboundPacket, message);

    /// <summary>Routes an inbound message to this player's handler modules.</summary>
    internal ValueTask Dispatch(IMessage message)
        => _registry.Dispatch(this, _modules, message);
}
