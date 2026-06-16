using System.Collections;
using System.Linq.Expressions;

namespace Starlight.Database.Querying;

public class StarlightQueryable<T> : IOrderedQueryable<T>
{
    internal StarlightQueryable(StarlightQueryProvider provider)
    {
        Provider = provider;
        Expression = Expression.Constant(this);
    }

    internal StarlightQueryable(StarlightQueryProvider provider, Expression expression)
    {
        Provider = provider;
        Expression = expression;
    }

    public Type ElementType => typeof(T);
    public Expression Expression { get; }
    public IQueryProvider Provider { get; }

    public IEnumerator<T> GetEnumerator()
    {
        var result = Provider.Execute<IEnumerable<T>>(Expression);
        return result.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public Task<List<T>> ToListAsync(CancellationToken cancellationToken = default)
        => ((StarlightQueryProvider)Provider).ToListAsync<T>(Expression, cancellationToken);

    public Task<T?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
        => ((StarlightQueryProvider)Provider).FirstOrDefaultAsync<T>(Expression, cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
        => ((StarlightQueryProvider)Provider).CountAsync(Expression, cancellationToken);

    public Task<bool> AnyAsync(CancellationToken cancellationToken = default)
        => ((StarlightQueryProvider)Provider).AnyAsync(Expression, cancellationToken);
}
