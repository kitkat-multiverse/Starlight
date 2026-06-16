using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _subscriptions.Add(await rpc.Subscribe<FetchPlayerReq>(GameSubjects.FetchPlayer, players.Fetch));
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
    public static IServiceCollection AddDbGate(this IServiceCollection collection, StarlightConfig config)
    {
        collection.AddDbContext<StarlightDbContext>(opts => {
            var provider = DatabaseHelper.ParseProvider(config.Database.ConnectionString, out var connString);
            switch (provider)
            {
                case ProviderType.Sqlite: {
                    connString = new SqliteConnectionStringBuilder {
                        DataSource = connString
                    }.ToString();

                    opts.UseSqlite(connString);
                    break;
                }
                default:
                    throw new NotSupportedException($"Unsupported or missing database provider '{provider?.ToString() ?? "<null>"}'.");
            }
        });

        collection
            .AddSingleton<PlayerService>();
        collection.AddHostedService<DbGateService>();

        return collection;
    }
}
