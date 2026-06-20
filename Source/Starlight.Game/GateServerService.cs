using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Starlight.Kcp;

namespace Starlight.Game;

public sealed class GateServerService(
    IConfiguration config,
    ILogger<GateServerService> logger
) : BackgroundService
{
    private GateConfig Config => config.GetSection("Game").Get<GateConfig>() ?? new GateConfig();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var handler = new GateServerHandler(logger);

            var server = new KcpServer(Config.BindAddress, Config.BindPort, handler);
            logger.LogInformation("Starting GameServer at {Address}:{Port}", Config.BindAddress, Config.BindPort);

            await server.RunAsync(stoppingToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "An error occured while trying to start GameServer!");
        }
    }
}

public sealed class GateServerHandler(ILogger logger) : IKcpServerHandler
{
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
