using System.Text;
using Starlight.Database.Metadata;
using Starlight.Database.Sql;

namespace Starlight.Database.Querying;

internal enum QueryTerminal
{
    Sequence,
    Count,
    LongCount,
    Any,
    First,
    FirstOrDefault,
    Single,
    SingleOrDefault
}

internal sealed class SqlQueryPlan(DatabaseModel model)
{
    public DatabaseModel Model { get; } = model;
    public List<string> Predicates { get; } = [];
    public List<string> Orderings { get; } = [];
    public int? Limit { get; set; }
    public int? Offset { get; set; }
    public QueryTerminal Terminal { get; set; } = QueryTerminal.Sequence;
    public SqliteParameterBag Parameters { get; } = new();

    public string BuildSelectSql(bool selectAllColumns = true)
    {
        var builder = new StringBuilder("SELECT ");

        builder.Append(selectAllColumns ? string.Join(", ", Model.Columns.Select(x => SqliteNames.QuoteIdentifier(x.ColumnName))) : "1");

        builder.Append(" FROM ").Append(SqliteNames.QuoteIdentifier(Model.TableName));
        AppendWhereOrderLimit(builder);
        return builder.ToString();
    }

    public string BuildCountSql()
    {
        var builder = new StringBuilder("SELECT COUNT(1) FROM ")
            .Append(SqliteNames.QuoteIdentifier(Model.TableName));
        AppendWhere(builder);
        return builder.ToString();
    }

    public string BuildAnySql()
    {
        var builder = new StringBuilder("SELECT EXISTS(SELECT 1 FROM ")
            .Append(SqliteNames.QuoteIdentifier(Model.TableName));
        AppendWhere(builder);
        builder.Append(" LIMIT 1)");
        return builder.ToString();
    }

    private void AppendWhereOrderLimit(StringBuilder builder)
    {
        AppendWhere(builder);

        if (Orderings.Count > 0)
            builder.Append(" ORDER BY ").Append(string.Join(", ", Orderings));

        if (Limit is not null)
            builder.Append(" LIMIT ").Append(Limit.Value);

        if (Offset is not null)
            builder.Append(" OFFSET ").Append(Offset.Value);
    }

    private void AppendWhere(StringBuilder builder)
    {
        if (Predicates.Count > 0)
            builder.Append(" WHERE ").Append(string.Join(" AND ", Predicates.Select(x => $"({x})")));
    }
}
