using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Starlight.Database.Metadata;

namespace Starlight.Database.Sql;

/// <summary>
/// Keeps each mapped table's schema in sync with its <see cref="DatabaseModel"/>.
/// </summary>
internal static class SqliteMigrator
{
    private const string LedgerTableName = "__StarlightSchema";

    public static async Task EnsureLedgerAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var sql =
            $"CREATE TABLE IF NOT EXISTS {SqliteNames.QuoteIdentifier(LedgerTableName)} (" +
            $"table_name TEXT PRIMARY KEY, " +
            $"schema_hash TEXT NOT NULL, " +
            $"applied_at TEXT NOT NULL);";

        await ExecuteAsync(connection, sql, cancellationToken);
    }

    public static async Task SynchronizeAsync(
        SqliteConnection connection,
        DatabaseModel model,
        CancellationToken cancellationToken
    )
    {
        var desiredHash = ComputeHash(SqliteSchemaBuilder.CreateTable(model));
        var liveColumns = await GetLiveColumnsAsync(connection, model.TableName, cancellationToken);

        if (liveColumns.Count == 0)
        {
            // Brand new table, just create it
            await ExecuteAsync(connection, SqliteSchemaBuilder.CreateTable(model), cancellationToken);
        }
        else if (ShapeMatches(liveColumns, model))
        {
            // Already up to date structurally
            // Nothing to migrate, but indexes below are still ensured.
        }
        else
        {
            // Existing table whose columns no longer match the model. Rebuild it atomically.
            await RebuildAsync(connection, model, liveColumns, cancellationToken);
        }

        // Creating new indexes (or the ones a rebuild wipes out) is idempotent thanks to IF NOT EXISTS.
        foreach (var indexSql in SqliteSchemaBuilder.CreateIndexes(model))
        {
            await ExecuteAsync(connection, indexSql, cancellationToken);
        }

        await SetLedgerAsync(connection, model.TableName, desiredHash, cancellationToken);
    }

    private static async Task RebuildAsync(
        SqliteConnection connection,
        DatabaseModel model,
        IReadOnlyList<LiveColumn> liveColumns,
        CancellationToken cancellationToken
    )
    {
        var backupTable = model.TableName + "_migrated";

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            // Clone the desired schema into a new table
            await ExecuteAsync(connection, SqliteSchemaBuilder.CreateTable(model, backupTable), cancellationToken, transaction);

            // Copy the data across the columns that exist in both old and new shapes
            // Columns dropped from the model are discarded, while newly added ones are left NULL/defaulted
            var shared = model.Columns
                .Select(x => x.ColumnName)
                .Where(name => liveColumns.Any(c => c.Name == name))
                .ToArray();

            if (shared.Length > 0)
            {
                var quoted = shared.Select(SqliteNames.QuoteIdentifier).ToArray();
                var copySql =
                    $"INSERT INTO {SqliteNames.QuoteIdentifier(backupTable)} ({string.Join(", ", quoted)}) " +
                    $"SELECT {string.Join(", ", quoted)} FROM {SqliteNames.QuoteIdentifier(model.TableName)};";
                await ExecuteAsync(connection, copySql, cancellationToken, transaction);
            }

            // Swap old -> new
            await ExecuteAsync(connection, $"DROP TABLE {SqliteNames.QuoteIdentifier(model.TableName)};", cancellationToken, transaction);
            await ExecuteAsync(connection,
                $"ALTER TABLE {SqliteNames.QuoteIdentifier(backupTable)} RENAME TO {SqliteNames.QuoteIdentifier(model.TableName)};",
                cancellationToken, transaction);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static bool ShapeMatches(IReadOnlyList<LiveColumn> live, DatabaseModel model)
    {
        if (live.Count != model.Columns.Count)
            return false;

        foreach (var column in model.Columns)
        {
            var liveColumn = live.FirstOrDefault(x => x.Name == column.ColumnName);

            if (liveColumn is null)
                return false;

            if (!string.Equals(liveColumn.Type, SqliteValueConverter.GetSqliteType(column), StringComparison.OrdinalIgnoreCase))
                return false;

            var expectedNotNull = column.IsPrimaryKey || column.IsRequired;
            if (liveColumn.NotNull != expectedNotNull)
                return false;

            if (liveColumn.PrimaryKey != (column.IsPrimaryKey ? 1 : 0))
                return false;
        }

        return true;
    }

    private static async Task<List<LiveColumn>> GetLiveColumnsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken
    )
    {
        var output = new List<LiveColumn>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({SqliteNames.QuoteIdentifier(tableName)});";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            output.Add(new LiveColumn {
                Name = reader.GetString(1),
                Type = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                NotNull = reader.GetInt32(3) != 0,
                PrimaryKey = reader.GetInt32(5)
            });
        }

        return output;
    }

    private static async Task<string?> GetLedgerHashAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT schema_hash FROM {SqliteNames.QuoteIdentifier(LedgerTableName)} WHERE table_name = $name;";
        command.Parameters.AddWithValue("$name", tableName);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    private static async Task SetLedgerAsync(
        SqliteConnection connection,
        string tableName,
        string schemaHash,
        CancellationToken cancellationToken
    )
    {
        var existing = await GetLedgerHashAsync(connection, tableName, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = existing is null
            ? $"INSERT INTO {SqliteNames.QuoteIdentifier(LedgerTableName)} (table_name, schema_hash, applied_at) VALUES ($name, $hash, $at);"
            : $"UPDATE {SqliteNames.QuoteIdentifier(LedgerTableName)} SET schema_hash = $hash, applied_at = $at WHERE table_name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        command.Parameters.AddWithValue("$hash", schemaHash);
        command.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    private sealed class LiveColumn
    {
        public required string Name { get; init; }
        public required string Type { get; init; }
        public bool NotNull { get; init; }
        public int PrimaryKey { get; init; }
    }
}
