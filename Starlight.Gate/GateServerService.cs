using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Starlight.Common;
using Starlight.Kcp;
using KcpLogLevel = Starlight.Kcp.LogLevel;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Starlight.Gate;

public sealed class GateServerService(
    ILogger<GateServerService> logger
) : BackgroundService, IKcpServerHandler
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            var server = new KcpServer(
                Config.Server.Game.BindAddress,
                Config.Server.Game.BindPort,
                LogMessage,
                this);

            logger.LogInformation("Starting GameServer at {Address}:{Port}",
                Config.Server.Game.BindAddress,
                Config.Server.Game.BindPort);

            await server.RunAsync(ct);
        }
        catch (Exception e)
        {
            logger.LogError(e, "An error occured while trying to start GameServer!");
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
        logger.LogInformation("Client connected: {Remote} (conv={Conv})", conn.Remote, conn.Conv);
    }

    public void OnDisconnected(KcpConnection conn)
    {
        logger.LogInformation("Client disconnected: {Remote} (conv={Conv})", conn.Remote, conn.Conv);
    }

    public void OnReceive(KcpConnection conn, byte[] data)
    {
        logger.LogDebug("Received {Length} bytes from {Remote}", data.Length, conn.Remote);
    }
}
