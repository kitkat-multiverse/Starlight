using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Starlight.Common;
using Starlight.DbGate.Services;
using Starlight.Rpc;
using Starlight.Rpc.Proto;

namespace Starlight.DbGate;

public sealed class DbGateService(
    RpcTransport rpc,
    PlayerService players
) : IHostedService
{
    private readonly HashSet<IDisposable> _subscriptions = [];

    public async Task StartAsync(CancellationToken cancellationToken) =>
        _subscriptions.Add(await rpc.Subscribe<FetchPlayerReq>(GameSubjects.FetchPlayer, players.Fetch));

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
