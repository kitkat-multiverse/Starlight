using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using Starlight.Database.Attributes;

namespace Starlight.Database.Metadata;

internal static class DatabaseModelCache
{
    private static readonly ConcurrentDictionary<Type, DatabaseModel> Models = new();

    public static DatabaseModel Get(Type type) => Models.GetOrAdd(type, BuildModel);

    public static DatabaseModel Get<T>() where T : class => Get(typeof(T));

    public static IReadOnlyList<DatabaseModel> Discover(params Assembly[] assemblies)
    {
        return assemblies
            .Where(x => !x.IsDynamic)
            .Distinct()
            .SelectMany(x => x.GetTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false } && t.GetCustomAttribute<DbTableAttribute>() is not null)
            .Select(Get)
            .OrderBy(x => x.TableName, StringComparer.Ordinal)
            .ToArray();
    }

    private static DatabaseModel BuildModel(Type type)
    {
        var tableName = type.GetCustomAttribute<DbTableAttribute>()?.Name;
        tableName = string.IsNullOrWhiteSpace(tableName) ? ToSnakeCase(type.Name) : tableName;

        var columns = type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p => p.GetMethod is { IsStatic: false } && p.GetCustomAttribute<DbIgnoreAttribute>() is null)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Where(p => p.GetMethod?.IsPublic == true || p.GetCustomAttribute<DbColumnAttribute>() is not null ||
                        p.GetCustomAttribute<DbPrimaryKeyAttribute>() is not null)
            .Select(BuildColumn)
            .ToArray();

        if (columns.Length == 0)
            throw new InvalidOperationException($"Database model '{type.FullName}' does not expose any mapped properties.");

        var primaryKeys = columns.Where(x => x.IsPrimaryKey).ToArray();

        if (primaryKeys.Length > 1)
            throw new InvalidOperationException(
                $"Database model '{type.FullName}' has multiple primary keys. The lightweight SQLite mapper supports one primary key per table.");

        var indexes = BuildIndexes(type, columns);

        return new DatabaseModel {
            ClrType = type,
            TableName = tableName!,
            Columns = columns,
            Indexes = indexes,
            PrimaryKey = primaryKeys.FirstOrDefault()
        };
    }

    private static DatabaseColumn BuildColumn(PropertyInfo property)
    {
        var column = property.GetCustomAttribute<DbColumnAttribute>();
        var key = property.GetCustomAttribute<DbPrimaryKeyAttribute>();
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        return new DatabaseColumn {
            Property = property,
            ColumnName = string.IsNullOrWhiteSpace(column?.Name) ? ToSnakeCase(property.Name) : column!.Name!,
            ClrType = property.PropertyType,
            StorageType = type,
            IsPrimaryKey = key is not null,
            AutoIncrement = key?.AutoIncrement == true,
            IsRequired = column?.IsRequired == true || key is not null && !property.PropertyType.IsNullable(),
            IsUnique = column?.IsUnique == true,
            StoreEnumAsText = column?.StoreEnumAsText == true,
            IsJson = property.GetCustomAttribute<DbJsonAttribute>() is not null,
            MaxLength = column?.MaxLength ?? 0,
            DefaultSql = column?.DefaultSql
        };
    }

    private static IReadOnlyList<DatabaseIndex> BuildIndexes(Type type, IReadOnlyList<DatabaseColumn> columns)
    {
        var indexes = new List<DatabaseIndex>();

        foreach (var property in columns.Select(x => x.Property))
        {
            foreach (var attr in property.GetCustomAttributes<DbIndexAttribute>())
            {
                indexes.Add(new DatabaseIndex {
                    Name = attr.Name,
                    IsUnique = attr.IsUnique,
                    Columns = [columns.Single(x => x.Property == property)]
                });
            }
        }

        foreach (var attr in type.GetCustomAttributes<DbIndexAttribute>())
        {
            var selected = attr.Properties
                .Select(p => columns.FirstOrDefault(x =>
                    string.Equals(x.Property.Name, p, StringComparison.Ordinal) ||
                    string.Equals(x.ColumnName, p, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            if (selected.Any(x => x is null))
            {
                var missing = attr.Properties.First(p => selected.All(x => x?.Property.Name != p && x?.ColumnName != p));

                throw new InvalidOperationException(
                    $"Index '{attr.Name}' on '{type.FullName}' references unmapped property or column '{missing}'.");
            }

            indexes.Add(new DatabaseIndex {
                Name = attr.Name,
                IsUnique = attr.IsUnique,
                Columns = selected.Cast<DatabaseColumn>().ToArray()
            });
        }

        return indexes;
    }

    internal static string ToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var builder = new StringBuilder(value.Length + 8);

        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];

            if (char.IsUpper(ch))
            {
                if (i > 0 && (char.IsLower(value[i - 1]) || i + 1 < value.Length && char.IsLower(value[i + 1])))
                    builder.Append('_');

                builder.Append(char.ToLowerInvariant(ch));
            } else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static bool IsNullable(this Type type)
    {
        if (!type.IsValueType)
            return true;

        return Nullable.GetUnderlyingType(type) is not null;
    }
}
