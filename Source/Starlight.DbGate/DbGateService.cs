using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Starlight.DbGate.Services;
using Starlight.Rpc;
using Starlight.Rpc.Proto;

namespace Starlight.DbGate;

public sealed class DbGateService(
    RpcTransport rpc,
    PlayerService players,
    StarlightDbContext db,
    ILogger<DbGateService> logger
) : IHostedService
{
    private readonly HashSet<IDisposable> _subscriptions = [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
#if DEBUG
        // Create the database.
        // TODO: Replace with real migrations once data models are done!
        await db.Database.EnsureCreatedAsync(cancellationToken);
#endif

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

        builder.Services.AddDbContext<StarlightDbContext>(opts => {
            switch (config.Provider)
            {
                case ProviderType.Sqlite: {
                    opts.UseSqlite(config.ConnectionString);
                    break;
                }
                default:
                    throw new NotSupportedException($"Unsupported or missing database provider '{config.Provider.ToString()}'.");
            }
        });

        builder.Services
            .AddSingleton<PlayerService>()
            .AddHostedService<DbGateService>();

        return builder;
    }
}
