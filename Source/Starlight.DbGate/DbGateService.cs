using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Starlight.Common;

namespace Starlight.DbGate;

public sealed class DbGateService : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }
}

public static class ServiceExtensions
{
    public static IServiceCollection AddDbGate(this IServiceCollection collection, StarlightConfig config)
    {
        collection.AddDbContext<StarlightDbContext>(opts => {
            switch (DatabaseHelper.ParseProvider(config.Database.ConnectionString, out var connString))
            {
                case ProviderType.Sqlite: {
                    connString = new SqliteConnectionStringBuilder {
                        DataSource = connString
                    }.ToString();

                    opts.UseSqlite(connString);
                    break;
                }
            }
        });

        collection.AddHostedService<DbGateService>();

        return collection;
    }
}
