using System.Linq.Expressions;
using Starlight.Database.ChangeTracking;
using Starlight.Database.Querying;

namespace Starlight.Database;

public interface IStarlightDatabase : IAsyncDisposable, IDisposable
{
    DatabaseSet<T> Set<T>() where T : class, new();

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task InitializeAsync(IEnumerable<Type> modelTypes, CancellationToken cancellationToken = default);

    void Add<T>(T entity) where T : class, new();
    void Attach<T>(T entity, EntityState state = EntityState.Unchanged) where T : class, new();
    void Remove<T>(T entity) where T : class, new();

    Task<T?> FindAsync<T>(object key, CancellationToken cancellationToken = default) where T : class, new();

    Task<List<T>> QueryAsync<T>(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
        where T : class, new();

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
