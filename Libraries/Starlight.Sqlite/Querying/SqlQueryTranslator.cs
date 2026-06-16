using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Starlight.Database.Metadata;
using Starlight.Database.Sql;

namespace Starlight.Database.Querying;

internal sealed class SqlQueryTranslator(Type rootType)
{
    private DatabaseModel Model { get; } = DatabaseModelCache.Get(rootType);

    public SqlQueryPlan Translate(Expression expression)
    {
        var plan = new SqlQueryPlan(Model);
        Visit(expression, plan);
        return plan;
    }

    private void Visit(Expression expression, SqlQueryPlan plan)
    {
        switch (expression)
        {
            case ConstantExpression constant when constant.Value is IQueryable queryable && queryable.ElementType == rootType:
                return;

            case MethodCallExpression method:
                VisitMethod(method, plan);
                return;

            default:
                throw new NotSupportedException($"Expression '{expression.NodeType}' is not supported by the SQLite translator.");
        }
    }

    private void VisitMethod(MethodCallExpression expression, SqlQueryPlan plan)
    {
        if (expression.Method.DeclaringType != typeof(Queryable))
            throw new NotSupportedException($"Method '{expression.Method.Name}' is not a Queryable LINQ method.");

        var name = expression.Method.Name;

        switch (name)
        {
            case nameof(Queryable.Where):
                Visit(expression.Arguments[0], plan);
                var whereLambda = UnquoteLambda(expression.Arguments[1]);
                plan.Predicates.Add(new PredicateSqlTranslator(Model, plan.Parameters).Translate(whereLambda.Body));
                return;

            case nameof(Queryable.OrderBy):
            case nameof(Queryable.OrderByDescending):
            case nameof(Queryable.ThenBy):
            case nameof(Queryable.ThenByDescending):
                Visit(expression.Arguments[0], plan);
                var orderLambda = UnquoteLambda(expression.Arguments[1]);
                var orderColumn = new PredicateSqlTranslator(Model, plan.Parameters).TranslateMemberAccess(orderLambda.Body);
                plan.Orderings.Add($"{orderColumn} {(name.EndsWith("Descending", StringComparison.Ordinal) ? "DESC" : "ASC")}");
                return;

            case nameof(Queryable.Skip):
                Visit(expression.Arguments[0], plan);
                plan.Offset = Convert.ToInt32(Evaluate(expression.Arguments[1]));
                return;

            case nameof(Queryable.Take):
                Visit(expression.Arguments[0], plan);
                plan.Limit = Convert.ToInt32(Evaluate(expression.Arguments[1]));
                return;

            case nameof(Queryable.Count):
            case nameof(Queryable.LongCount):
                Visit(expression.Arguments[0], plan);

                if (expression.Arguments.Count == 2)
                {
                    var countLambda = UnquoteLambda(expression.Arguments[1]);
                    plan.Predicates.Add(new PredicateSqlTranslator(Model, plan.Parameters).Translate(countLambda.Body));
                }
                plan.Terminal = name == nameof(Queryable.Count) ? QueryTerminal.Count : QueryTerminal.LongCount;
                return;

            case nameof(Queryable.Any):
                Visit(expression.Arguments[0], plan);

                if (expression.Arguments.Count == 2)
                {
                    var anyLambda = UnquoteLambda(expression.Arguments[1]);
                    plan.Predicates.Add(new PredicateSqlTranslator(Model, plan.Parameters).Translate(anyLambda.Body));
                }
                plan.Terminal = QueryTerminal.Any;
                return;

            case nameof(Queryable.First):
            case nameof(Queryable.FirstOrDefault):
            case nameof(Queryable.Single):
            case nameof(Queryable.SingleOrDefault):
                Visit(expression.Arguments[0], plan);

                if (expression.Arguments.Count == 2)
                {
                    var lambda = UnquoteLambda(expression.Arguments[1]);
                    plan.Predicates.Add(new PredicateSqlTranslator(Model, plan.Parameters).Translate(lambda.Body));
                }
                plan.Limit = name.StartsWith("Single", StringComparison.Ordinal) ? 2 : 1;

                plan.Terminal = name switch {
                    nameof(Queryable.First) => QueryTerminal.First,
                    nameof(Queryable.FirstOrDefault) => QueryTerminal.FirstOrDefault,
                    nameof(Queryable.Single) => QueryTerminal.Single,
                    _ => QueryTerminal.SingleOrDefault
                };
                return;

            case nameof(Queryable.Select):
                var selectLambda = UnquoteLambda(expression.Arguments[1]);

                if (selectLambda.Body == selectLambda.Parameters[0])
                {
                    Visit(expression.Arguments[0], plan);
                    return;
                }

                throw new NotSupportedException(
                    "Projection is evaluated client-side. SQL projection support intentionally stays conservative to preserve type safety.");

            default:
                throw new NotSupportedException($"Queryable.{name} is not translated to SQLite.");
        }
    }

    private static LambdaExpression UnquoteLambda(Expression expression)
    {
        while (expression.NodeType == ExpressionType.Quote)
            expression = ((UnaryExpression)expression).Operand;

        return (LambdaExpression)expression;
    }

    private static object? Evaluate(Expression expression)
    {
        if (expression is ConstantExpression constant)
            return constant.Value;

        var lambda = Expression.Lambda<Func<object?>>(Expression.Convert(expression, typeof(object)));
        return lambda.Compile().Invoke();
    }
}

internal sealed class PredicateSqlTranslator(DatabaseModel model, SqliteParameterBag parameters) : ExpressionVisitor
{
    public string Translate(Expression expression) => VisitSql(expression);

    public string TranslateMemberAccess(Expression expression)
    {
        expression = StripConvert(expression);

        if (expression is not MemberExpression member)
            throw new NotSupportedException("ORDER BY expressions must reference a mapped property.");

        var column = ResolveColumn(member);

        if (column is null)
            throw new NotSupportedException("ORDER BY expressions must reference a mapped property.");

        return SqliteNames.QuoteIdentifier(column.ColumnName);
    }

    private string VisitSql(Expression expression)
    {
        expression = StripConvert(expression);

        return expression switch {
            BinaryExpression binary => VisitBinarySql(binary),
            MemberExpression member => VisitMemberSql(member),
            ConstantExpression constant => parameters.Add(constant.Value),
            MethodCallExpression call => VisitMethodSql(call),
            UnaryExpression unary when unary.NodeType == ExpressionType.Not => $"NOT ({VisitSql(unary.Operand)})",
            UnaryExpression unary => VisitSql(unary.Operand),
            _ => throw new NotSupportedException($"Predicate expression '{expression.NodeType}' is not supported.")
        };
    }

    private string VisitBinarySql(BinaryExpression binary)
    {
        var left = StripConvert(binary.Left);
        var right = StripConvert(binary.Right);

        var leftIsNull = IsNullConstant(left);
        var rightIsNull = IsNullConstant(right);

        if (binary.NodeType is ExpressionType.Equal or ExpressionType.NotEqual && (leftIsNull || rightIsNull))
        {
            var nonNullSide = leftIsNull ? right : left;
            return $"{VisitSql(nonNullSide)} {(binary.NodeType == ExpressionType.Equal ? "IS" : "IS NOT")} NULL";
        }

        var op = binary.NodeType switch {
            ExpressionType.Equal => "=",
            ExpressionType.NotEqual => "<>",
            ExpressionType.GreaterThan => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            ExpressionType.AndAlso => "AND",
            ExpressionType.OrElse => "OR",
            ExpressionType.Add => "+",
            ExpressionType.Subtract => "-",
            ExpressionType.Multiply => "*",
            ExpressionType.Divide => "/",
            _ => throw new NotSupportedException($"Binary operator '{binary.NodeType}' is not supported.")
        };

        if (binary.NodeType is ExpressionType.Equal or ExpressionType.NotEqual or ExpressionType.GreaterThan
            or ExpressionType.GreaterThanOrEqual or ExpressionType.LessThan or ExpressionType.LessThanOrEqual)
        {
            var leftColumn = ResolveColumnExpression(left);
            var rightColumn = ResolveColumnExpression(right);

            if (leftColumn is not null && rightColumn is null)
                return $"({VisitSql(left)} {op} {parameters.Add(Evaluate(right), leftColumn)})";

            if (rightColumn is not null && leftColumn is null)
                return $"({parameters.Add(Evaluate(left), rightColumn)} {op} {VisitSql(right)})";
        }

        return $"({VisitSql(left)} {op} {VisitSql(right)})";
    }

    private string VisitMemberSql(MemberExpression member)
    {
        var column = ResolveColumn(member);

        if (column is not null)
            return SqliteNames.QuoteIdentifier(column.ColumnName);

        return parameters.Add(Evaluate(member));
    }

    private string VisitMethodSql(MethodCallExpression call)
    {
        if (call.Object?.Type == typeof(string))
            return VisitStringMethodSql(call);

        if (call.Method.DeclaringType == typeof(Enumerable) && call.Method.Name == nameof(Enumerable.Contains))
            return VisitEnumerableContainsSql(call);

        if (call.Method.Name == "Contains" && call.Object is not null && call.Object.Type != typeof(string) &&
            typeof(IEnumerable).IsAssignableFrom(call.Object.Type))
            return VisitInstanceContainsSql(call);

        throw new NotSupportedException($"Method '{call.Method.Name}' cannot be translated to SQLite.");
    }

    private string VisitStringMethodSql(MethodCallExpression call)
    {
        var target = VisitSql(call.Object!);
        var argument = Evaluate(call.Arguments[0])?.ToString() ?? string.Empty;

        return call.Method.Name switch {
            nameof(string.Contains) => $"{target} LIKE {parameters.Add($"%{EscapeLike(argument)}%")} ESCAPE '\\'",
            nameof(string.StartsWith) => $"{target} LIKE {parameters.Add($"{EscapeLike(argument)}%")} ESCAPE '\\'",
            nameof(string.EndsWith) => $"{target} LIKE {parameters.Add($"%{EscapeLike(argument)}")} ESCAPE '\\'",
            nameof(string.Equals) => $"{target} = {parameters.Add(argument)}",
            _ => throw new NotSupportedException($"String method '{call.Method.Name}' cannot be translated to SQLite.")
        };
    }

    private string VisitEnumerableContainsSql(MethodCallExpression call)
    {
        var values = (IEnumerable?)Evaluate(call.Arguments[0])
                     ?? throw new NotSupportedException("Enumerable.Contains requires a non-null enumerable.");
        var item = call.Arguments[1];
        var itemSql = VisitSql(item);
        return BuildInSql(itemSql, values.Cast<object?>(), ResolveColumnExpression(item));
    }

    private string VisitInstanceContainsSql(MethodCallExpression call)
    {
        var values = (IEnumerable?)Evaluate(call.Object!)
                     ?? throw new NotSupportedException("Contains requires a non-null enumerable.");
        var item = call.Arguments[0];
        var itemSql = VisitSql(item);
        return BuildInSql(itemSql, values.Cast<object?>(), ResolveColumnExpression(item));
    }

    private string BuildInSql(string itemSql, IEnumerable<object?> values, DatabaseColumn? itemColumn)
    {
        var parameterNames = values.Select(v => parameters.Add(v, itemColumn)).ToArray();

        if (parameterNames.Length == 0)
            return "0 = 1";

        return $"{itemSql} IN ({string.Join(", ", parameterNames)})";
    }

    private DatabaseColumn? ResolveColumnExpression(Expression expression)
    {
        expression = StripConvert(expression);
        return expression is MemberExpression member ? ResolveColumn(member) : null;
    }

    private DatabaseColumn? ResolveColumn(MemberExpression member)
    {
        if (member.Member is not PropertyInfo property)
            return null;

        var expression = StripConvert(member.Expression!);

        if (expression is not ParameterExpression || expression.Type != model.ClrType)
            return null;

        return model.Columns.FirstOrDefault(x => x.Property.Name == property.Name);
    }

    private static Expression StripConvert(Expression expression)
    {
        while (expression.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked)
            expression = ((UnaryExpression)expression).Operand;

        return expression;
    }

    private static bool IsNullConstant(Expression expression)
        => expression is ConstantExpression { Value: null };

    private static object? Evaluate(Expression expression)
    {
        if (expression is ConstantExpression constant)
            return constant.Value;

        var lambda = Expression.Lambda<Func<object?>>(Expression.Convert(expression, typeof(object)));
        return lambda.Compile().Invoke();
    }

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
