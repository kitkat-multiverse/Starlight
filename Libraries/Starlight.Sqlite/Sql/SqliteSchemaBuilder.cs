using System.Text;
using Starlight.Database.Metadata;

namespace Starlight.Database.Sql;

internal static class SqliteSchemaBuilder
{
    public static string CreateTable(DatabaseModel model)
    {
        var builder = new StringBuilder();

        builder.Append("CREATE TABLE IF NOT EXISTS ")
            .Append(SqliteNames.QuoteIdentifier(model.TableName))
            .Append(" (");

        for (var i = 0; i < model.Columns.Count; i++)
        {
            var column = model.Columns[i];

            if (i > 0)
                builder.Append(", ");

            builder.Append(SqliteNames.QuoteIdentifier(column.ColumnName))
                .Append(' ')
                .Append(SqliteValueConverter.GetSqliteType(column));

            if (column.IsPrimaryKey)
            {
                builder.Append(" PRIMARY KEY");

                if (column.AutoIncrement)
                    builder.Append(" AUTOINCREMENT");
            }

            if (column.IsRequired && !column.IsPrimaryKey)
                builder.Append(" NOT NULL");

            if (column.IsUnique && !column.IsPrimaryKey)
                builder.Append(" UNIQUE");

            if (column.MaxLength > 0)
                builder.Append(" CHECK(length(").Append(SqliteNames.QuoteIdentifier(column.ColumnName)).Append(") <= ")
                    .Append(column.MaxLength).Append(')');

            if (!string.IsNullOrWhiteSpace(column.DefaultSql))
                builder.Append(" DEFAULT ").Append(column.DefaultSql);
        }

        builder.Append(");");
        return builder.ToString();
    }

    public static IEnumerable<string> CreateIndexes(DatabaseModel model)
    {
        foreach (var index in model.Indexes)
        {
            yield return
                $"CREATE {(index.IsUnique ? "UNIQUE " : string.Empty)}INDEX IF NOT EXISTS {SqliteNames.QuoteIdentifier(index.Name)} ON {SqliteNames.QuoteIdentifier(model.TableName)} ({string.Join(", ", index.Columns.Select(x => SqliteNames.QuoteIdentifier(x.ColumnName)))});";
        }
    }
}
