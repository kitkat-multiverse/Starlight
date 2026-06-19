using Microsoft.Extensions.Hosting;

namespace Starlight.Game;

public sealed class GameServerService : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => throw new NotImplementedException();
}
