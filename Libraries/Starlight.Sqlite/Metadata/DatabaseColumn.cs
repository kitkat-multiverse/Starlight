using System.Reflection;
using Starlight.Database.Attributes;

namespace Starlight.Database.Metadata;

internal sealed class DatabaseColumn
{
    public required PropertyInfo Property { get; init; }
    public required string ColumnName { get; init; }
    public required Type ClrType { get; init; }
    public required Type StorageType { get; init; }
    public bool IsPrimaryKey { get; init; }
    public bool AutoIncrement { get; init; }
    public bool IsRequired { get; init; }
    public bool IsUnique { get; init; }
    public bool StoreEnumAsText { get; init; }
    public bool IsJson { get; init; }
    public int MaxLength { get; init; }
    public string? DefaultSql { get; init; }

    public object? GetValue(object entity) => Property.GetValue(entity);

    public void SetValue(object entity, object? value)
    {
        if (Property.SetMethod is null)
            return;

        Property.SetValue(entity, value);
    }
}

internal sealed class DatabaseIndex
{
    public required string Name { get; init; }
    public required IReadOnlyList<DatabaseColumn> Columns { get; init; }
    public bool IsUnique { get; init; }
}

internal sealed class DatabaseModel
{
    public required Type ClrType { get; init; }
    public required string TableName { get; init; }
    public required IReadOnlyList<DatabaseColumn> Columns { get; init; }
    public required IReadOnlyList<DatabaseIndex> Indexes { get; init; }
    public DatabaseColumn? PrimaryKey { get; init; }

    public DatabaseColumn GetColumn(string propertyName)
    {
        var column = Columns.FirstOrDefault(x => x.Property.Name == propertyName);

        if (column is null)
            throw new InvalidOperationException($"Property '{ClrType.Name}.{propertyName}' is not mapped to a database column.");

        return column;
    }

    public bool TryGetColumn(string propertyName, out DatabaseColumn? column)
    {
        column = Columns.FirstOrDefault(x => x.Property.Name == propertyName);
        return column is not null;
    }
}
