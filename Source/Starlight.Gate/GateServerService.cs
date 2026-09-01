using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Starlight.Common;
using Starlight.Crypto.Client;
using Starlight.Ec2b;
using Starlight.Gate.Session;
using Starlight.Kcp;
using Starlight.Protobuf.Registry;
using Starlight.Rpc;
using Starlight.Rpc.Proto;
using Starlight.Rpc.Tunnel;
using Starlight.Rpc.Tunnel.Connection;
using System.Collections.Concurrent;
using KcpLogLevel = Starlight.Kcp.LogLevel;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Starlight.Gate;

public sealed class GateServerService(
    RpcTransport rpc,
    ClientCrypto crypto,
    ProtocolRegistryProvider registryProvider,
    ITunnelConnector connector,
    IConfiguration config,
    ILogger<GateServerService> logger
)
    : BackgroundService, IKcpServerHandler
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    private readonly Lazy<GateConfig> _config = new(() => config.GetSection("Gate").Get<GateConfig>() ?? new GateConfig());
    private readonly ConcurrentDictionary<KcpConnection, INetworkSession> _sessions = new();

    public GateConfig Config => _config.Value;
    public RpcTransport Rpc => rpc;
    public ClientCrypto ClientCrypto => crypto;
    public TunnelClient Tunnel { get; } = new(rpc, connector);

    public ProtocolRegistryProvider Registry { get; } = registryProvider;
    public byte[] ServerKey { get; private set; } = [];

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // From the region ID, derive the client secret & XOR key.
        var secret = Ec2bKeyGen.Create(Config.RegionId);
        ServerKey = Ec2bHelper.Derive(secret);

        _ = Task.Run(() => HeartbeatTask(ct), ct);

        try
        {
            using var server = new KcpServer(Config.BindAddress, Config.BindPort, LogMessage, this);

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

                await Rpc.Publish(GateSubjects.ServerHeartbeat, new GateHeartbeatNotify {
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

        logger.LogInformation(
            "Client disconnected: {Remote} (conv={Conv}, reason={Reason}, reasonCode={ReasonCode}, pendingSendSegments={PendingSendSegments})",
            conn.Remote,
            conn.Conv,
            (DisconnectReason)reason,
            reason,
            conn.PendingSendSegments);
    }

    public void OnReceive(KcpConnection conn, byte[] data)
    {
        if (_sessions.TryGetValue(conn, out var session))
        {
            session.Receive(data);
        }

        logger.LogTrace("Received {Length} bytes from {Remote}", data.Length, conn.Remote);
    }
}

public static class GateServerExtensions
{
    public static IHostApplicationBuilder AddGateServer(this IHostApplicationBuilder builder, params ProtocolRegistry[] registries)
    {
        var config = builder.Configuration.GetSection("Gate").Get<GateConfig>() ?? new GateConfig();

        builder.TrySetSigningKeyPath("Gate server", config.Keys.SigningKeyPath);
        builder.TrySetSdkKeyPath("Gate server", config.Keys.SdkKeyPath);

        builder.Services
            .AddSingleton(new ProtocolRegistryProvider(registries))
            .AddHostedService<GateServerService>();
        return builder;
    }
}
