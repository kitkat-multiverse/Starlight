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
    IServiceScopeFactory scopes,
    ILogger<DbGateService> logger
) : IHostedService
{
    private readonly HashSet<IDisposable> _subscriptions = [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
#if DEBUG
        // Create the database.
        // TODO: Replace with real migrations once data models are done!
        // The context is scoped, so it can't be injected into this singleton directly.
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StarlightDbContext>();

            if (!await db.Database.EnsureCreatedAsync(cancellationToken) && await HasDriftedAsync(db, cancellationToken))
            {
                logger.LogWarning("The database schema no longer matches the data models; rebuilding it from scratch.");

                await db.Database.EnsureDeletedAsync(cancellationToken);
                await db.Database.EnsureCreatedAsync(cancellationToken);
            }
        }
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

#if DEBUG
    /// <summary>
    /// Compares every mapped table against the columns SQLite actually has.
    /// </summary>
    /// <remarks>
    /// <see cref="RelationalDatabaseFacadeExtensions"/>' <c>EnsureCreated</c> does nothing once the
    /// file has tables, so a database written by an older model keeps its old columns and every
    /// insert dies on a <c>NOT NULL</c> the current model never fills in.
    /// </remarks>
    private static async Task<bool> HasDriftedAsync(StarlightDbContext db, CancellationToken cancellationToken)
    {
        // Owned types share their owner's table, so the expected columns are grouped, not per-entity.
        var tables = db.Model.GetEntityTypes()
            .Where(entity => entity.GetTableName() is not null)
            .GroupBy(entity => entity.GetTableName()!)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(entity => entity.GetProperties())
                    .Select(property => property.GetColumnName())
                    .ToHashSet());

        await db.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            foreach (var (table, expected) in tables)
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT name FROM pragma_table_info($table);";

                var parameter = command.CreateParameter();
                parameter.ParameterName = "$table";
                parameter.Value = table;
                command.Parameters.Add(parameter);

                var actual = new HashSet<string>();
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    actual.Add(reader.GetString(0));
                }

                // A missing table counts too; EnsureCreated won't add one to a populated file.
                if (!actual.SetEquals(expected))
                    return true;
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return false;
    }
#endif
}

public static class ServiceExtensions
{
    public static IHostApplicationBuilder AddDbGate(this IHostApplicationBuilder builder)
    {
        var config = builder.Configuration.GetSection("DbGate").Get<DbGateConfig>() ?? new DbGateConfig();

        builder.Services.AddDbContext<StarlightDbContext>(opts => {
            switch (config.Provider)
            {
                case ProviderType.Sqlite:
                    {
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
