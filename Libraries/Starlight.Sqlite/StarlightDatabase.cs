using System.Collections.Concurrent;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Serilog;
using Starlight.Database.ChangeTracking;
using Starlight.Database.Metadata;
using Starlight.Database.Querying;
using Starlight.Database.Sql;

namespace Starlight.Database;

public sealed class StarlightDatabase(StarlightDatabaseOptions options) : IStarlightDatabase
{
    private readonly ConcurrentDictionary<object, EntityEntry> _entries = new(ReferenceEqualityComparer.Instance);
    private readonly SemaphoreSlim _sync = new(initialCount: 1, maxCount: 1);
    private SqliteConnection? _connection;
    private bool _disposed;

    internal StarlightDatabaseOptions Options => options;

    public DatabaseSet<T> Set<T>() where T : class, new() => new(this);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var models = DatabaseModelCache.Discover(options.ModelAssemblies
            .DefaultIfEmpty(Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).ToArray());
        await InitializeAsync(models.Select(x => x.ClrType), cancellationToken);
    }

    public async Task InitializeAsync(IEnumerable<Type> modelTypes, CancellationToken cancellationToken = default)
    {
        await EnsureOpenAsync(cancellationToken);

        var models = modelTypes.Distinct().Select(DatabaseModelCache.Get).ToArray();

        if (models.Length == 0)
        {
            Log.Information("SQLite database initialized with no discovered models.");
            return;
        }

        await _sync.WaitAsync(cancellationToken);

        try
        {
            foreach (var model in models)
            {
                await ExecuteNonQueryLockedAsync(SqliteSchemaBuilder.CreateTable(model), parameterBag: null, cancellationToken);

                foreach (var indexSql in SqliteSchemaBuilder.CreateIndexes(model))
                {
                    await ExecuteNonQueryLockedAsync(indexSql, parameterBag: null, cancellationToken);
                }
            }
        }
        finally
        {
            _sync.Release();
        }

        Log.Information("SQLite database initialized with {ModelCount} model(s) at {Path}.", models.Length, options.Path);
    }

    public void Add<T>(T entity) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(entity);
        Track(entity, EntityState.Added);
    }

    public void Attach<T>(T entity, EntityState state = EntityState.Unchanged) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(entity);
        Track(entity, state);
    }

    public void Remove<T>(T entity) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(entity);
        Track(entity, EntityState.Deleted);
    }

    public async Task<T?> FindAsync<T>(object key, CancellationToken cancellationToken = default) where T : class, new()
    {
        var model = DatabaseModelCache.Get<T>();
        var primaryKey = model.PrimaryKey ?? throw new InvalidOperationException($"Model '{typeof(T).Name}' does not have a primary key.");

        var parameterBag = new SqliteParameterBag();
        var where = $"{SqliteNames.QuoteIdentifier(primaryKey.ColumnName)} = {parameterBag.Add(key, primaryKey)}";

        var sql =
            $"SELECT {string.Join(", ", model.Columns.Select(x => SqliteNames.QuoteIdentifier(x.ColumnName)))} FROM {SqliteNames.QuoteIdentifier(model.TableName)} WHERE {where} LIMIT 1";

        var rows = await ExecuteEntityQueryAsync<T>(sql, parameterBag, cancellationToken);
        return rows.FirstOrDefault();
    }

    public async Task<List<T>> QueryAsync<T>(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
        where T : class, new()
    {
        IQueryable<T> query = Set<T>();

        if (predicate is not null)
            query = query.Where(predicate);

        return await ((StarlightQueryable<T>)query).ToListAsync(cancellationToken);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureOpenAsync(cancellationToken);

        var entries = _entries.Values
            .Where(x => x.State is not EntityState.Unchanged and not EntityState.Detached || x.HasChanges())
            .ToArray();

        if (entries.Length == 0)
            return 0;

        await _sync.WaitAsync(cancellationToken);

        try
        {
            await using var transaction = (SqliteTransaction)await _connection!.BeginTransactionAsync(cancellationToken);
            var affected = 0;

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                affected += entry.State switch {
                    EntityState.Added => await InsertLockedAsync(entry, transaction, cancellationToken),
                    EntityState.Deleted => await DeleteLockedAsync(entry, transaction, cancellationToken),
                    _ => await UpdateLockedAsync(entry, transaction, cancellationToken)
                };
            }

            await transaction.CommitAsync(cancellationToken);

            foreach (var entry in entries)
            {
                if (entry.State == EntityState.Deleted)
                {
                    _entries.TryRemove(entry.Entity, out _);
                    continue;
                }

                entry.AcceptChanges();
            }

            return affected;
        }
        finally
        {
            _sync.Release();
        }
    }

    internal async Task<object?> ExecuteTranslatedQueryAsync(
        Type rootType,
        Expression expression,
        Type requestedType,
        CancellationToken cancellationToken
    )
    {
        await EnsureOpenAsync(cancellationToken);

        var translator = new SqlQueryTranslator(rootType);
        var plan = translator.Translate(expression);

        return plan.Terminal switch {
            QueryTerminal.Count => await ExecuteScalarAsync<int>(plan.BuildCountSql(), plan.Parameters, cancellationToken),
            QueryTerminal.LongCount => await ExecuteScalarAsync<long>(plan.BuildCountSql(), plan.Parameters, cancellationToken),
            QueryTerminal.Any => await ExecuteScalarAsync<long>(plan.BuildAnySql(), plan.Parameters, cancellationToken) != 0,
            QueryTerminal.First or QueryTerminal.FirstOrDefault or QueryTerminal.Single or QueryTerminal.SingleOrDefault =>
                await ExecuteTerminalEntityAsync(rootType, plan, cancellationToken),
            _ => await ExecuteSequenceAsync(rootType, requestedType, plan, cancellationToken)
        };
    }

    internal IEnumerable LoadAllForClientEvaluation(Type rootType)
    {
        var method = typeof(StarlightDatabase)
            .GetMethod(nameof(LoadAllForClientEvaluationGeneric), BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(rootType);

        return (IEnumerable)method.Invoke(this, [])!;
    }

    private List<T> LoadAllForClientEvaluationGeneric<T>() where T : class, new()
        => ExecuteEntityQueryAsync<T>(BuildSelectAllSql(DatabaseModelCache.Get<T>()), parameterBag: null, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    private async Task<object?> ExecuteSequenceAsync(Type rootType, Type requestedType, SqlQueryPlan plan, CancellationToken cancellationToken)
    {
        var elementType = requestedType.GetSequenceElementType();

        if (elementType != rootType)
            throw new NotSupportedException("The LINQ projection is not directly translatable to SQLite.");

        var method = typeof(StarlightDatabase)
            .GetMethod(nameof(ExecuteEntityPlanAsync), BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(rootType);

        return await (Task<object>)method.Invoke(this, [plan, cancellationToken])!;
    }

    private async Task<object> ExecuteEntityPlanAsync<T>(SqlQueryPlan plan, CancellationToken cancellationToken) where T : class, new()
        => await ExecuteEntityQueryAsync<T>(plan.BuildSelectSql(), plan.Parameters, cancellationToken);

    private async Task<object?> ExecuteTerminalEntityAsync(Type rootType, SqlQueryPlan plan, CancellationToken cancellationToken)
    {
        var method = typeof(StarlightDatabase)
            .GetMethod(nameof(ExecuteTerminalEntityGenericAsync), BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(rootType);

        return await (Task<object?>)method.Invoke(this, [plan, cancellationToken])!;
    }

    private async Task<object?> ExecuteTerminalEntityGenericAsync<T>(SqlQueryPlan plan, CancellationToken cancellationToken)
        where T : class, new()
    {
        var rows = await ExecuteEntityQueryAsync<T>(plan.BuildSelectSql(), plan.Parameters, cancellationToken);

        return plan.Terminal switch {
            QueryTerminal.First => rows.First(),
            QueryTerminal.FirstOrDefault => rows.FirstOrDefault(),
            QueryTerminal.Single => rows.Single(),
            QueryTerminal.SingleOrDefault => rows.SingleOrDefault(),
            _ => rows.FirstOrDefault()
        };
    }

    private async Task<List<T>> ExecuteEntityQueryAsync<T>(string sql, SqliteParameterBag? parameterBag, CancellationToken cancellationToken)
        where T : class, new()
    {
        await EnsureOpenAsync(cancellationToken);
        await _sync.WaitAsync(cancellationToken);

        try
        {
            await using var command = _connection!.CreateCommand();
            command.CommandText = sql;
            parameterBag?.Apply(command);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var model = DatabaseModelCache.Get<T>();
            var output = new List<T>();

            while (await reader.ReadAsync(cancellationToken))
            {
                var entity = new T();

                foreach (var column in model.Columns)
                {
                    var ordinal = reader.GetOrdinal(column.ColumnName);
                    var raw = await reader.IsDBNullAsync(ordinal, cancellationToken) ? null : reader.GetValue(ordinal);
                    column.SetValue(entity, SqliteValueConverter.FromDatabase(raw, column));
                }

                Track(entity, EntityState.Unchanged);
                output.Add(entity);
            }

            return output;
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task<T> ExecuteScalarAsync<T>(string sql, SqliteParameterBag parameterBag, CancellationToken cancellationToken)
    {
        await EnsureOpenAsync(cancellationToken);
        await _sync.WaitAsync(cancellationToken);

        try
        {
            await using var command = _connection!.CreateCommand();
            command.CommandText = sql;
            parameterBag.Apply(command);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is null || value is DBNull ? default! : (T)Convert.ChangeType(value, typeof(T));
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task<int> InsertLockedAsync(EntityEntry entry, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var columns = entry.Model.Columns
            .Where(x => !x.AutoIncrement || !IsDefaultValue(x.GetValue(entry.Entity)))
            .ToArray();

        var parameterBag = new SqliteParameterBag();
        string sql;

        if (columns.Length == 0)
        {
            sql = $"INSERT INTO {SqliteNames.QuoteIdentifier(entry.Model.TableName)} DEFAULT VALUES;";
        } else
        {
            var names = columns.Select(x => SqliteNames.QuoteIdentifier(x.ColumnName)).ToArray();
            var values = columns.Select(x => parameterBag.Add(x.GetValue(entry.Entity), x)).ToArray();

            sql =
                $"INSERT INTO {SqliteNames.QuoteIdentifier(entry.Model.TableName)} ({string.Join(", ", names)}) VALUES ({string.Join(", ", values)});";
        }

        var affected = await ExecuteNonQueryLockedAsync(sql, parameterBag, cancellationToken, transaction);

        if (entry.Model.PrimaryKey is { AutoIncrement: true } key)
        {
            await using var command = _connection!.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT last_insert_rowid();";
            var raw = await command.ExecuteScalarAsync(cancellationToken);
            key.SetValue(entry.Entity, Convert.ChangeType(raw, key.StorageType));
        }

        return affected;
    }

    private async Task<int> UpdateLockedAsync(EntityEntry entry, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var key = entry.Model.PrimaryKey ??
                  throw new InvalidOperationException(
                      $"Model '{entry.Model.ClrType.Name}' cannot be updated because it does not have a primary key.");
        var changed = entry.GetChangedColumns().Where(x => !x.IsPrimaryKey).ToArray();

        if (changed.Length == 0)
            return 0;

        var parameterBag = new SqliteParameterBag();

        var setters = changed
            .Select(x => $"{SqliteNames.QuoteIdentifier(x.ColumnName)} = {parameterBag.Add(x.GetValue(entry.Entity), x)}")
            .ToArray();
        var keyParameter = parameterBag.Add(key.GetValue(entry.Entity), key);

        var sql =
            $"UPDATE {SqliteNames.QuoteIdentifier(entry.Model.TableName)} SET {string.Join(", ", setters)} WHERE {SqliteNames.QuoteIdentifier(key.ColumnName)} = {keyParameter};";
        return await ExecuteNonQueryLockedAsync(sql, parameterBag, cancellationToken, transaction);
    }

    private async Task<int> DeleteLockedAsync(EntityEntry entry, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var key = entry.Model.PrimaryKey ??
                  throw new InvalidOperationException(
                      $"Model '{entry.Model.ClrType.Name}' cannot be deleted because it does not have a primary key.");
        var parameterBag = new SqliteParameterBag();
        var keyParameter = parameterBag.Add(key.GetValue(entry.Entity), key);

        var sql =
            $"DELETE FROM {SqliteNames.QuoteIdentifier(entry.Model.TableName)} WHERE {SqliteNames.QuoteIdentifier(key.ColumnName)} = {keyParameter};";
        return await ExecuteNonQueryLockedAsync(sql, parameterBag, cancellationToken, transaction);
    }

    private EntityEntry Track(object entity, EntityState state)
    {
        var model = DatabaseModelCache.Get(entity.GetType());

        var entry = _entries.GetOrAdd(entity, static (item, args) => new EntityEntry(item, args.Model, args.State),
            (Model: model, State: state));
        entry.State = state;
        return entry;
    }

    private async Task EnsureOpenAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null)
            return;

        if (options.CreateIfMissing)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(options.Path));

            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
        }

        var builder = new SqliteConnectionStringBuilder {
            DataSource = options.Path,
            Mode = options.CreateIfMissing ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        };

        _connection = new SqliteConnection(builder.ToString());
        await _connection.OpenAsync(cancellationToken);

        await using var command = _connection.CreateCommand();

        command.CommandText =
            $"PRAGMA foreign_keys = ON; PRAGMA busy_timeout = {options.BusyTimeoutMilliseconds}; PRAGMA synchronous = {options.Synchronous};";
        await command.ExecuteNonQueryAsync(cancellationToken);

        if (options.UseWal)
        {
            command.CommandText = "PRAGMA journal_mode = WAL;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task<int> ExecuteNonQueryLockedAsync(
        string sql,
        SqliteParameterBag? parameterBag,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null
    )
    {
        await using var command = _connection!.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        parameterBag?.Apply(command);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string BuildSelectAllSql(DatabaseModel model)
        =>
            $"SELECT {string.Join(", ", model.Columns.Select(x => SqliteNames.QuoteIdentifier(x.ColumnName)))} FROM {SqliteNames.QuoteIdentifier(model.TableName)}";

    private static bool IsDefaultValue(object? value)
    {
        if (value is null)
            return true;

        var type = value.GetType();
        return type.IsValueType && value.Equals(Activator.CreateInstance(type));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _connection?.Dispose();
        _sync.Dispose();
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        if (_connection is not null)
            await _connection.DisposeAsync();

        _sync.Dispose();
        _disposed = true;
    }

    private sealed class EntityEntry
    {
        private readonly Dictionary<DatabaseColumn, object?> _originalValues;

        public EntityEntry(object entity, DatabaseModel model, EntityState state)
        {
            Entity = entity;
            Model = model;
            State = state;
            _originalValues = Snapshot();

            if (entity is IDirtyTrackable trackable)
                trackable.AcceptChanges();
        }

        public object Entity { get; }
        public DatabaseModel Model { get; }
        public EntityState State { get; set; }

        public bool HasChanges() => State == EntityState.Modified || GetChangedColumns().Any();

        public IReadOnlyList<DatabaseColumn> GetChangedColumns()
        {
            if (State == EntityState.Added)
                return Model.Columns;

            if (State == EntityState.Deleted)
                return [];

            if (Entity is IDirtyTrackable trackable && trackable.DirtyProperties.Count > 0)
            {
                return trackable.DirtyProperties
                    .Select(p => Model.TryGetColumn(p, out var column) ? column : null)
                    .Where(x => x is not null)
                    .Cast<DatabaseColumn>()
                    .ToArray();
            }

            return Model.Columns
                .Where(column => !Equals(SqliteValueConverter.ToDatabase(column.GetValue(Entity), column), _originalValues[column]))
                .ToArray();
        }

        public void AcceptChanges()
        {
            State = EntityState.Unchanged;
            _originalValues.Clear();

            foreach (var pair in Snapshot())
            {
                _originalValues[pair.Key] = pair.Value;
            }

            if (Entity is IDirtyTrackable trackable)
                trackable.AcceptChanges();
        }

        private Dictionary<DatabaseColumn, object?> Snapshot()
            => Model.Columns.ToDictionary(column => column, column => SqliteValueConverter.ToDatabase(column.GetValue(Entity), column));
    }

    // Kazusa was here
}
