using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Starlight.Common;
using Starlight.Gate.Session;
using Starlight.Kcp;
using Starlight.Rpc;
using Starlight.Rpc.Proto;
using KcpLogLevel = Starlight.Kcp.LogLevel;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Starlight.Gate;

public sealed class GateServerService(
    RpcTransport rpc,
    IConfiguration config,
    ILogger<GateServerService> logger
) : BackgroundService, IKcpServerHandler
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<KcpConnection, INetworkSession> _sessions = new();
    private readonly Lazy<GateConfig> _config = new(() => config.GetSection("Gate").Get<GateConfig>() ?? new GateConfig());

    private GateConfig Config => _config.Value;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
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
                    ExternalAddress = Config.ServingLocal ? "127.0.0.1" : await SystemHelper.PublicIpAddress(),
                    ExternalPort = Config.ServePort,
                    Sessions = { /* TODO: Add all connected sessions here. */ }
                };

                var regionInfo = new StarlightRegionInfo {
                    RegionId = Config.Region.Identifier,
                    RegionName = Config.Region.DisplayName
                };

                await rpc.Publish(GateSubjects.ServerHeartbeat, new GateHeartbeatNotify {
                    ServerInfo = serverInfo, RegionInfo = regionInfo
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
        _sessions[conn] = new StarlightSession(conn);

        logger.LogDebug("Client connected: {Remote} (conv={Conv})", conn.Remote, conn.Conv);
    }

    public void OnDisconnected(KcpConnection conn, uint reason)
    {
        if (_sessions.TryRemove(conn, out var session))
        {
            session.OnClose(reason);
        }

        logger.LogInformation("Client disconnected: {Remote} (conv={Conv})", conn.Remote, conn.Conv);
    }

    public void OnReceive(KcpConnection conn, byte[] data)
    {
        logger.LogDebug("Received {Length} bytes from {Remote}", data.Length, conn.Remote);
    }
}
