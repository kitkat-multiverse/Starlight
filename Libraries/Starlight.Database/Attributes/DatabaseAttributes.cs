namespace Starlight.Database.Attributes;

/// <summary>
/// Maps a CLR type to a SQLite table using the specified name. If omitted, the type name is converted to snake_case.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class DbTableAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}

/// <summary>
/// Same as <see cref="DbTableAttribute"/> but for records. Records are classes, so this is just a semantic convenience.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class DbColumnAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
    public bool IsRequired { get; init; }
    public bool IsUnique { get; init; }
    public bool StoreEnumAsText { get; init; }
    public int MaxLength { get; init; }
    public string? DefaultSql { get; init; }
}

/// <summary>
/// Marks a property as the primary key for a table
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class DbPrimaryKeyAttribute : Attribute
{
    public bool AutoIncrement { get; init; }
}

/// <summary>
/// Excludes a CLR property from persistence.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event, Inherited = true)]
public sealed class DbIgnoreAttribute : Attribute;

/// <summary>
/// Stores the property as JSON in a TEXT column using the shared application JSON settings
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class DbJsonAttribute : Attribute;

/// <summary>
/// Creates an index for a table. Apply to a property for a single-column index or to the class for multi-column indexes.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = true)]
public sealed class DbIndexAttribute(string name, params string[] properties) : Attribute
{
    public string Name { get; } = name;
    public string[] Properties { get; } = properties;
    public bool IsUnique { get; init; }
}
