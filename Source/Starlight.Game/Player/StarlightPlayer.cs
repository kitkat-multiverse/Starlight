using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Starlight.Game.Modules;
using Starlight.Kcp;
using Starlight.Protocol;
using Starlight.Rpc;
using Starlight.Rpc.Proto;
using Starlight.Rpc.Tunnel;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Game.Player;

public sealed class StarlightPlayer : IPlayer
{
    private readonly ModuleRegistry _registry;
    private readonly RpcTunnel _tunnel;
    private readonly IModule[] _modules;
    private readonly ILogger<StarlightPlayer> _logger;

    public StarlightPlayer(IServiceProvider provider, ModuleRegistry registry, RpcTunnel tunnel)
    {
        _registry = registry;
        _tunnel = tunnel;
        _modules = registry.CreateModules(provider, this);
        _logger = provider.GetRequiredService<ILogger<StarlightPlayer>>();
    }

    public uint Uid { get; set; }
    public string AccountUid { get; set; } = string.Empty;
    public CancellationToken Closing => _tunnel.Closed;
    public NetPlayerState State { get; set; } = new();
    public object StateLock { get; } = new();

    /// <inheritdoc/>
    public TModule Module<TModule>() where TModule : class, IModule
        => (TModule)_modules[_registry.IndexOf<TModule>()];

    /// <inheritdoc/>
    public Task Send(IMessage message)
        => _tunnel.Publish(GameSubjects.OutboundPacket, message);

    /// <inheritdoc/>
    public ValueTask Emit(LifecycleEvent @event)
        => _registry.Dispatch(this, _modules, @event);

    /// <summary>Routes an inbound message to this player's handler modules.</summary>
    internal async ValueTask Dispatch(IMessage message)
    {
        try
        {
            await _registry.Dispatch(this, _modules, message);
        }
        catch (KickException kick)
        {
            // Awaited in turn: overlapping the publishes lets a flush-disconnect land ahead of
            // the replies it is supposed to flush.
            foreach (var reply in kick.Replies)
            {
                await Send(reply);
            }

            await Disconnect(kick.Reason, kick.Flush);
        }
        catch (Exception ex)
        {
            // Nothing below us answers the client, so a fault here would hang them forever.
            _logger.LogError(ex, "Unhandled error dispatching {Message} for player '{PlayerId}'",
                message.GetType().Name, Uid);

            await Disconnect((uint)DisconnectReason.ServerKick, flush: false);
        }
    }

    /// <summary>Runs the disconnect lifecycle. The tunnel is already gone, so faults can only be logged.</summary>
    internal async Task Close()
    {
        try
        {
            await Emit(LifecycleEvent.PlayerSaving);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error saving player '{PlayerId}'", Uid);
        }

        try
        {
            await Emit(LifecycleEvent.PlayerDisconnect);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error tearing down player '{PlayerId}'", Uid);
        }
    }

    private Task Disconnect(uint reason, bool flush)
        => _tunnel.Publish(GameSubjects.Disconnect, new DisconnectNotify {
            Reason = reason,
            Flush = flush
        });
}
