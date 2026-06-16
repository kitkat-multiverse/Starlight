using Starlight.Database.ChangeTracking;

namespace Starlight.Database.Querying;

public sealed class DatabaseSet<T> : StarlightQueryable<T> where T : class, new()
{
    internal DatabaseSet(StarlightDatabase database) : base(new StarlightQueryProvider(database, typeof(T)))
    {
        Database = database;
    }

    public StarlightDatabase Database { get; }

    public void Add(T entity) => Database.Add(entity);
    public void Attach(T entity, EntityState state = EntityState.Unchanged) => Database.Attach(entity, state);
    public void Remove(T entity) => Database.Remove(entity);

    public Task<T?> FindAsync(object key, CancellationToken cancellationToken = default)
        => Database.FindAsync<T>(key, cancellationToken);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => Database.SaveChangesAsync(cancellationToken);
}
