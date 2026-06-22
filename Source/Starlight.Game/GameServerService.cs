using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Starlight.Protobuf.Core;
using Starlight.Rpc;
using Starlight.Rpc.Proto;
using Starlight.Rpc.Tunnel;
using Starlight.Rpc.Tunnel.Connection;

namespace Starlight.Game;

public sealed class GameServerService(
    RpcTransport rpc,
    ITunnelAcceptor acceptor,
    ILogger<GameServerService> logger) : BackgroundService
{
    private readonly HashSet<IDisposable> _subs = [];

    public TunnelHost Tunnel { get; } = new(rpc, acceptor);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _subs.Add(await Tunnel.Listen(GameSubjects.GateConnection));
        Tunnel.TunnelOpened += OnTunnelOpened;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var sub in _subs)
        {
            sub.Dispose();
        }
        return Task.CompletedTask;
    }

    private Task OnTunnelOpened(RpcTunnel tunnel, NewTunnelReq msg)
    {
        // TODO: Use `metadata` to pass the UID & IP address of the connecting user.
        logger.LogInformation("Opened gate server connection.");

        // TODO: Dispose when connection collapses.
        //       Use `RpcTunnel#OnClosed` for this.
        tunnel.Subscribe(GameSubjects.InboundPacket, inbound => {
            var packet = inbound.Decode<IMessage>();
            // TODO: Resolve serializer from `metadata`.
            logger.LogInformation("Received packet from server: {Packet}",
                packet.GetType().Name);
            return Task.CompletedTask;
        });

        return Task.CompletedTask;
    }
}
