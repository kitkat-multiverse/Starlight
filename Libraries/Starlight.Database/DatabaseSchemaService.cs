using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Starlight.Database;

/// <summary>
/// Brings <typeparamref name="TContext"/>'s schema into existence on startup.
/// </summary>
/// <remarks>
/// TODO: Replace with real migrations once the data models are done!
/// </remarks>
internal sealed class DatabaseSchemaService<TContext>(
    IServiceScopeFactory scopes,
    ILogger<DatabaseSchemaService<TContext>> logger
) : IHostedService where TContext : DbContext
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // The context is scoped, so it can't be injected into this singleton directly.
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        EnsureDataSourceDirectory(db);

        if (await db.Database.EnsureCreatedAsync(cancellationToken) || !await HasDriftedAsync(db, cancellationToken))
            return;

        logger.LogWarning("The {Context} schema no longer matches the data models.", typeof(TContext).Name);

#if DEBUG
        // Iterating on the models shouldn't cost you the accounts you were testing with,
        // so the stale file is moved aside rather than dropped.
        var archived = await ArchiveDataSourceAsync(db, cancellationToken);

        logger.LogWarning("Rebuilt it from scratch; the old database is at '{Archived}'.", archived);

        await db.Database.EnsureCreatedAsync(cancellationToken);
#else
        // Refusing to start beats limping along: SQLite matches column names case-insensitively,
        // so a stale file half-works and then dies mid-request on the first column that moved.
        throw new InvalidOperationException(
            $"The {typeof(TContext).Name} database does not match the current data models. " +
            "Move it aside (or add migrations) before starting again.");
#endif
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // SQLite opens a file, it doesn't create the folder around it, so a connection string
    // pointing at something like ./data/accounts.db fails on a fresh checkout.
    private static void EnsureDataSourceDirectory(TContext db)
    {
        if (DataSourcePath(db) is not {} path)
            return;

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }

    // Null for anything we can't treat as a plain path: another provider, an in-memory
    // database, or a file: URI whose path is tangled up with query parameters.
    private static string? DataSourcePath(TContext db)
    {
        if (!db.Database.IsSqlite())
            return null;

        var source = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;

        return string.IsNullOrEmpty(source)
               || source == ":memory:"
               || source.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ?
            null :
            source;
    }

#if DEBUG
    // Renames the database out of the way and returns where it went, so the rebuild that
    // follows starts from nothing without destroying what was there.
    private static async Task<string> ArchiveDataSourceAsync(TContext db, CancellationToken cancellationToken)
    {
        var path = DataSourcePath(db)
                   ?? throw new InvalidOperationException("Only a SQLite file can be archived.");
        var archived = $"{path}.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.bak";

        await db.Database.CloseConnectionAsync();

        // Pooled connections keep a handle on the file, and the -wal/-shm siblings hold
        // commits that haven't been folded in yet; leaving either behind corrupts the copy.
        SqliteConnection.ClearAllPools();

        File.Move(path, archived);

        foreach (var suffix in (string[])["-wal", "-shm"])
        {
            if (File.Exists(path + suffix))
                File.Move(path + suffix, archived + suffix);
        }

        return archived;
    }
#endif

    // EnsureCreated does nothing once the file has tables, so a database written by an older
    // model keeps its old columns and every insert dies on a NOT NULL nothing fills in.
    private static async Task<bool> HasDriftedAsync(TContext db, CancellationToken cancellationToken)
    {
        if (!db.Database.IsSqlite())
            return false;

        // Owned types share their owner's table, so expected columns are grouped, not per-entity.
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
}
