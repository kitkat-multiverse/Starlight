using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Starlight.Game.Modules;
using Starlight.Game.Player;
using Starlight.Protobuf.Core;
using Starlight.Rpc;
using Starlight.Rpc.Proto;
using Starlight.Rpc.Tunnel;
using Starlight.Rpc.Tunnel.Connection;

namespace Starlight.Game;

public sealed class GameServerService(
    IServiceProvider services,
    RpcTransport rpc,
    ITunnelAcceptor acceptor,
    ModuleRegistry modules,
    ILogger<GameServerService> logger
) : BackgroundService
{
    private readonly HashSet<IDisposable> _subs = [];

    public TunnelHost Tunnel { get; } = new(rpc, acceptor);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Listen last: it starts accepting tunnels immediately, and one that lands before the
        // handler is attached would be accepted with nothing subscribed to its packets.
        Tunnel.TunnelOpened += OnTunnelOpened;
        _subs.Add(await Tunnel.Listen(GameSubjects.GateConnection));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        foreach (var sub in _subs)
        {
            sub.Dispose();
        }

        Tunnel.Dispose();
    }

    private Task OnTunnelOpened(RpcTunnel tunnel, NewTunnelReq msg)
    {
        try
        {
            var sessionInfo = PlayerConnectNotify.Parser.ParseFrom(msg.Metadata);

            logger.LogInformation("Opened gate server connection for '{AccountId}' with {RemoteIp}:{RemotePort}.",
                sessionInfo.Uid, sessionInfo.RemoteAddr, sessionInfo.RemotePort);

            var player = new StarlightPlayer(services, modules, tunnel);

            // Route inbound packets to the player's handler modules.
            var listener = tunnel.Subscribe(GameSubjects.InboundPacket, async inbound => {
                var message = inbound.Decode<IMessage>();
                await player.Dispatch(message);
            });

            tunnel.OnClosed += () => {
                logger.LogDebug("Closed gate server connection with {RemoteIp}:{RemotePort}.",
                    sessionInfo.RemoteAddr, sessionInfo.RemotePort);

                listener.Dispose();
            };
        }
        catch (Exception ex)
        {
            // TunnelHost needs the throw: it closes the local end and reports the error back to
            // the gate. Swallowing it leaves the gate holding a tunnel nobody is listening on.
            logger.LogWarning(ex, "Failed to accept gate server connection");
            throw;
        }

        return Task.CompletedTask;
    }
}

public static class GameServerExtensions
{
    /// Adds the <see cref="GameServerService"/> with a requirement of having an
    /// immutable <see cref="ModuleRegistry"/> configured.
    public static IHostApplicationBuilder AddGameServer(this IHostApplicationBuilder builder, ModuleRegistry registry)
    {
        if (!registry.Immutable)
        {
            throw new ArgumentException("ModuleRegistry must be immutable when adding GameServerService.", nameof(registry));
        }

        builder.Services
            .AddSingleton(registry)
            .AddHostedService<GameServerService>();
        return builder;
    }
}
