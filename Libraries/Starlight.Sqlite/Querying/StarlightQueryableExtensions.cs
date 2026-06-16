namespace Starlight.Database.Querying;

public static class StarlightQueryableExtensions
{
    public static Task<List<T>> ToListAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
        => query is StarlightQueryable<T> starlight ? starlight.ToListAsync(cancellationToken) : Task.FromResult(query.ToList());

    public static Task<T?> FirstOrDefaultAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
        => query is StarlightQueryable<T> starlight ?
            starlight.FirstOrDefaultAsync(cancellationToken) :
            Task.FromResult(query.FirstOrDefault());

    public static Task<int> CountAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
        => query is StarlightQueryable<T> starlight ? starlight.CountAsync(cancellationToken) : Task.FromResult(query.Count());

    public static Task<bool> AnyAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
        => query is StarlightQueryable<T> starlight ? starlight.AnyAsync(cancellationToken) : Task.FromResult(query.Any());
}
