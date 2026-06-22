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
using Starlight.Rpc.Tunnel;
using Starlight.Rpc.Tunnel.Connection;
using KcpLogLevel = Starlight.Kcp.LogLevel;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Starlight.Gate;

public sealed class GateServerService : BackgroundService, IKcpServerHandler
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    private readonly RpcTransport _rpc;
    private readonly Lazy<GateConfig> _config;
    private readonly ILogger<GateServerService> _logger;

    private readonly ConcurrentDictionary<KcpConnection, INetworkSession> _sessions = new();

    private CancellationToken _ct = CancellationToken.None;

    public GateServerService(
        RpcTransport rpc,
        ProtocolRegistryProvider registryProvider,
        ITunnelConnector connector,
        IConfiguration config,
        ILogger<GateServerService> logger
    )
    {
        _rpc = rpc;
        _config = new Lazy<GateConfig>(() => config.GetSection("Gate").Get<GateConfig>() ?? new GateConfig());
        _logger = logger;

        Registry = registryProvider;
        Tunnel = new TunnelClient(rpc, connector);
    }

    public GateConfig Config => _config.Value;
    public TunnelClient Tunnel { get; }

    public ProtocolRegistryProvider Registry { get; }
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

            _logger.LogInformation("Starting GameServer at {Address}:{Port}",
                Config.BindAddress, Config.BindPort);

            await server.RunAsync(ct);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "An error occured while trying to start GameServer!");
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

                await _rpc.Publish(GateSubjects.ServerHeartbeat, new GateHeartbeatNotify {
                    ServerInfo = serverInfo, RegionId = Config.RegionId
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish server heartbeat");
            }

            await Task.Delay(HeartbeatInterval, ct);
        }
    }

    private void LogMessage(KcpLogLevel level, string message, params object[] args)
    {
        _logger.Log(level switch {
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

        _logger.LogDebug("Client connected: {Remote} (conv={Conv})", conn.Remote, conn.Conv);
    }

    public void OnDisconnected(KcpConnection conn, uint reason)
    {
        if (_sessions.TryRemove(conn, out var session))
        {
            session.OnClose(reason);
        }

        _logger.LogDebug("Client disconnected: {Remote} (conv={Conv})", conn.Remote, conn.Conv);
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
                    _logger.LogWarning(ex, "Failed to handle packet for {Remote}", conn.Remote);
                }
            }, _ct);
        }

        _logger.LogTrace("Received {Length} bytes from {Remote}", data.Length, conn.Remote);
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
