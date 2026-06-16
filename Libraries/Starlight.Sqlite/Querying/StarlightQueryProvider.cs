using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace Starlight.Database.Querying;

internal sealed class StarlightQueryProvider(StarlightDatabase database, Type rootType) : IQueryProvider
{
    public IQueryable CreateQuery(Expression expression)
    {
        var elementType = expression.Type.GetSequenceElementType() ?? expression.Type;
        var queryableType = typeof(StarlightQueryable<>).MakeGenericType(elementType);

        return (IQueryable)Activator.CreateInstance(queryableType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, [this, expression], culture: null)!;
    }

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        => new StarlightQueryable<TElement>(this, expression);

    public object? Execute(Expression expression)
        => ExecuteAsync(expression, typeof(object), CancellationToken.None).GetAwaiter().GetResult();

    public TResult Execute<TResult>(Expression expression)
    {
        var result = ExecuteAsync(expression, typeof(TResult), CancellationToken.None).GetAwaiter().GetResult();
        return result is null ? default! : (TResult)result;
    }

    public async Task<List<T>> ToListAsync<T>(Expression expression, CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(expression, typeof(IEnumerable<T>), cancellationToken);

        return result switch {
            List<T> list => list,
            IEnumerable<T> enumerable => enumerable.ToList(),
            null => [],
            _ => throw new InvalidOperationException($"Query returned '{result.GetType().Name}', not a sequence of '{typeof(T).Name}'.")
        };
    }

    public async Task<T?> FirstOrDefaultAsync<T>(Expression expression, CancellationToken cancellationToken)
    {
        var firstCall = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.FirstOrDefault),
            [typeof(T)],
            expression);

        var result = await ExecuteAsync(firstCall, typeof(T), cancellationToken);
        return result is null ? default : (T?)result;
    }

    public async Task<int> CountAsync(Expression expression, CancellationToken cancellationToken)
    {
        var elementType = expression.Type.GetSequenceElementType() ?? rootType;

        var countCall = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Count),
            [elementType],
            expression);

        var result = await ExecuteAsync(countCall, typeof(int), cancellationToken);
        return result is null ? 0 : (int)result;
    }

    public async Task<bool> AnyAsync(Expression expression, CancellationToken cancellationToken)
    {
        var elementType = expression.Type.GetSequenceElementType() ?? rootType;

        var anyCall = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Any),
            [elementType],
            expression);

        var result = await ExecuteAsync(anyCall, typeof(bool), cancellationToken);
        return result is bool value && value;
    }

    internal async Task<object?> ExecuteAsync(Expression expression, Type requestedType, CancellationToken cancellationToken)
    {
        try
        {
            return await database.ExecuteTranslatedQueryAsync(rootType, expression, requestedType, cancellationToken);
        }
        catch (NotSupportedException) when (database.Options.AllowClientEvaluation)
        {
            return ExecuteClientSide(expression);
        }
    }

    private object? ExecuteClientSide(Expression expression)
    {
        var rootData = database.LoadAllForClientEvaluation(rootType);
        var replacement = rootData.AsQueryable();
        var rewritten = new QueryRootRewriter(rootType, replacement.Expression).Visit(expression)!;

        var lambda = Expression.Lambda(rewritten);
        return lambda.Compile().DynamicInvoke();
    }

    private sealed class QueryRootRewriter(Type rootType, Expression replacement) : ExpressionVisitor
    {
        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value is IQueryable queryable && queryable.ElementType == rootType)
            {
                var starlightQueryableType = typeof(StarlightQueryable<>).MakeGenericType(rootType);

                if (starlightQueryableType.IsAssignableFrom(queryable.GetType()))
                    return replacement;
            }

            return base.VisitConstant(node);
        }
    }
}

internal static class QueryTypeExtensions
{
    public static Type? GetSequenceElementType(this Type type)
    {
        if (type == typeof(string))
            return null;

        if (type.IsArray)
            return type.GetElementType();

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            return type.GetGenericArguments()[0];

        return type.GetInterfaces()
            .Where(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .Select(x => x.GetGenericArguments()[0])
            .FirstOrDefault();
    }
}
