using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Starlight.Database;
using Starlight.DbGate.Services;
using Starlight.Rpc;
using Starlight.Rpc.Proto;

namespace Starlight.DbGate;

public sealed class DbGateService(
    RpcTransport rpc,
    PlayerService players,
    ILogger<DbGateService> logger
) : IHostedService
{
    private readonly HashSet<IDisposable> _subscriptions = [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _subscriptions.Add(await rpc.Subscribe<FetchPlayerReq>(GameSubjects.FetchPlayer, players.Fetch));
        logger.LogInformation("DB Gate is now listening for requests...");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
        return Task.CompletedTask;
    }
}

public static class ServiceExtensions
{
    public static IHostApplicationBuilder AddDbGate(this IHostApplicationBuilder builder)
    {
        var config = builder.Configuration.GetSection("DbGate").Get<DbGateConfig>() ?? new DbGateConfig();

        builder.Services
            .AddStarlightDbContext<StarlightDbContext>(config)
            .AddSingleton<PlayerService>()
            .AddHostedService<DbGateService>();

        return builder;
    }
}
