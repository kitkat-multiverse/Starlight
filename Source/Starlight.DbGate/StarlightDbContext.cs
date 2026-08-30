using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Starlight.DbGate.Models;

namespace Starlight.DbGate;

/// <summary>
/// When marked on a property, it is serialized as JSON as applicable.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class JsonColumnAttribute : Attribute;

public sealed class StarlightDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Player> Players { get; set; } = null!;
    public DbSet<PlayerProfile> Profiles { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        #region JSON Column Support

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var properties = entity.GetProperties()
                .Where(p => p.PropertyInfo?.GetCustomAttribute<JsonColumnAttribute>() is not null);

            foreach (var property in properties)
            {
                modelBuilder.Entity(entity.ClrType)
                    .OwnsOne(property.ClrType, property.Name, nav => nav.ToJson());
            }
        }

        #endregion

        modelBuilder.Entity<Player>()
            .HasAlternateKey(p => p.AccountId);
    }
}

/// <summary>
/// Preserves early-development databases when player state is introduced. The generic schema
/// checker intentionally archives drifted files, but this change is safely additive.
/// </summary>
internal sealed class PlayerStateSchemaUpgradeService(
    IServiceScopeFactory scopes,
    ILogger<PlayerStateSchemaUpgradeService> logger
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StarlightDbContext>();

        if (!db.Database.IsSqlite())
            return;

        await db.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var tableCommand = db.Database.GetDbConnection().CreateCommand();

            tableCommand.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Players';";

            if (Convert.ToInt32(await tableCommand.ExecuteScalarAsync(cancellationToken)) == 0)
                return;

            await using var columnsCommand = db.Database.GetDbConnection().CreateCommand();
            columnsCommand.CommandText = "SELECT name FROM pragma_table_info('Players');";

            var hasState = false;

            await using (var reader = await columnsCommand.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (reader.GetString(0).Equals("State", StringComparison.OrdinalIgnoreCase))
                    {
                        hasState = true;
                        break;
                    }
                }
            }

            if (!hasState)
            {
                await db.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE Players ADD COLUMN State BLOB NOT NULL DEFAULT X'';",
                    cancellationToken);
            }
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1) // SQLITE_ERROR
        {
            // Let the normal schema service produce its detailed drift/startup error.
        }
        catch (SqliteException ex)
        {
            logger.LogWarning(ex, "Player state schema upgrade failed, falling back to the generic schema service.");
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
