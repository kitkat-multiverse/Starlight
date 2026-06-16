using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Starlight.Common.Config;
using Starlight.Kcp;

namespace Starlight.Game;

public sealed class GateServerService(
    ILogger<GateServerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var handler = new GateServerHandler(logger);
            var server = new KcpServer(
                Config.Server.Game.BindAddress,
                Config.Server.Game.BindPort,
                handler);

            logger.LogInformation("Starting GameServer at {0}:{1}",
                Config.Server.Game.BindAddress,
                Config.Server.Game.BindPort);
            
            await server.RunAsync(stoppingToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "An error occured while trying to start GameServer!");
        }
    }
}

public sealed class GateServerHandler : IKcpServerHandler
{
    private readonly ILogger _logger;

    public GateServerHandler(ILogger logger)
    {
        _logger = logger;
    }

    public void OnConnected(KcpConnection conn)
    {
        _logger.LogInformation("Client connected: {Remote} (conv={Conv})", conn.Remote, conn.Conv);
    }

    public void OnDisconnected(KcpConnection conn)
    {
        _logger.LogInformation("Client disconnected: {Remote} (conv={Conv})", conn.Remote, conn.Conv);
    }

    public void OnReceive(KcpConnection conn, byte[] data)
    {
        _logger.LogDebug("Received {Length} bytes from {Remote}", data.Length, conn.Remote);
    }
}
