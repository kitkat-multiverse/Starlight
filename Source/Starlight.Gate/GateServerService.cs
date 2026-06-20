using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Starlight.Common;
using Starlight.Ec2b;
using Starlight.Gate.Session;
using Starlight.Kcp;
using Starlight.Protobuf.Registry;
using Starlight.Rpc;
using Starlight.Rpc.Proto;
using KcpLogLevel = Starlight.Kcp.LogLevel;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Starlight.Gate;

public sealed class GateServerService(
    RpcTransport rpc,
    ProtocolRegistryProvider registryProvider,
    IConfiguration config,
    ILogger<GateServerService> logger
) : BackgroundService, IKcpServerHandler
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<KcpConnection, INetworkSession> _sessions = new();
    private readonly Lazy<GateConfig> _config = new(() => config.GetSection("Gate").Get<GateConfig>() ?? new GateConfig());

    public GateConfig Config => _config.Value;

    private CancellationToken _ct = CancellationToken.None;

    public ProtocolRegistryProvider Registry => registryProvider;
    public byte[] ServerKey { get; private set; } = [];

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _ct = ct;

        // From the region ID, derive the client secret & XOR key.
        var secret = Ec2bKeyGen.Create(Config.RegionId);
        ServerKey = Ec2bHelper.Derive(secret);

        _ = Task.Run(() => HeartbeatTask(ct), ct);

        try
        {
            var server = new KcpServer(Config.BindAddress, Config.BindPort, LogMessage, this);

            logger.LogInformation("Starting GameServer at {Address}:{Port}",
                Config.BindAddress, Config.BindPort);

            await server.RunAsync(ct);
        }
        catch (Exception e)
        {
            logger.LogError(e, "An error occured while trying to start GameServer!");
        }
    }

    private async Task HeartbeatTask(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var serverInfo = new GateServerInfo {
                    ServerId = Config.ServerId,
                    ExternalAddress = Config.ServingLocal ? "127.0.0.1" : await SystemHelper.PublicIpAddress(ct),
                    ExternalPort = Config.ServePort,
                    Sessions = {
                        /* TODO: Add all connected sessions here. */
                    }
                };

                await rpc.Publish(GateSubjects.ServerHeartbeat, new GateHeartbeatNotify {
                    ServerInfo = serverInfo, RegionId = Config.RegionId
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to publish server heartbeat");
            }

            await Task.Delay(HeartbeatInterval, ct);
        }
    }

    private void LogMessage(KcpLogLevel level, string message, params object[] args)
    {
        logger.Log(level switch {
            KcpLogLevel.Verbose => LogLevel.Trace,
            KcpLogLevel.Debug => LogLevel.Debug,
            KcpLogLevel.Information => LogLevel.Information,
            KcpLogLevel.Warning => LogLevel.Warning,
            KcpLogLevel.Error => LogLevel.Error,
            _ => throw new ArgumentException("Unknown log level", nameof(level))
#pragma warning disable CA2254
        }, message, args);
#pragma warning restore CA2254
    }

    public void OnConnected(KcpConnection conn)
    {
        _sessions[conn] = new StarlightSession(this, conn);

        logger.LogDebug("Client connected: {Remote} (conv={Conv})", conn.Remote, conn.Conv);
    }

    public void OnDisconnected(KcpConnection conn, uint reason)
    {
        if (_sessions.TryRemove(conn, out var session))
        {
            session.OnClose(reason);
        }

        logger.LogDebug("Client disconnected: {Remote} (conv={Conv})", conn.Remote, conn.Conv);
    }

    public void OnReceive(KcpConnection conn, byte[] data)
    {
        if (_sessions.TryGetValue(conn, out var session))
        {
            Task.Run(async () => {
                try
                {
                    await session.HandlePacket(data);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to handle packet for {Remote}", conn.Remote);
                }
            }, _ct);
        }

        logger.LogTrace("Received {Length} bytes from {Remote}", data.Length, conn.Remote);
    }
}

public static class GateServerExtensions
{
    public static IHostApplicationBuilder AddGateServer(this IHostApplicationBuilder builder, params ProtocolRegistry[] registries)
    {
        builder.Services
            .AddSingleton(new ProtocolRegistryProvider(registries))
            .AddHostedService<GateServerService>();
        return builder;
    }
}
