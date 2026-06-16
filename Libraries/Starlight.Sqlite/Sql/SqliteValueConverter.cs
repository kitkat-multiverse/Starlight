using System.Globalization;
using System.Text.Json;
using Starlight.Common;
using Starlight.Database.Metadata;

namespace Starlight.Database.Sql;

internal static class SqliteValueConverter
{
    public static string GetSqliteType(DatabaseColumn column)
    {
        var type = column.StorageType;

        if (column.IsJson)
            return "TEXT";

        if (type.IsEnum)
            return column.StoreEnumAsText ? "TEXT" : "INTEGER";

        if (type == typeof(string) || type == typeof(char) || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset))
            return "TEXT";

        if (type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
            type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) || type == typeof(TimeSpan))
            return "INTEGER";

        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return "REAL";

        if (type == typeof(byte[]))
            return "BLOB";

        if (column.IsJson)
            return "TEXT";

        throw new NotSupportedException($"Property '{column.Property.DeclaringType?.Name}.{column.Property.Name}' uses unsupported type '{column.ClrType}'. Add [DbJson] if it should be serialized as JSON.");
    }

    public static object? ToDatabase(object? value, DatabaseColumn? column = null)
    {
        if (value is null)
            return null;

        if (column?.IsJson == true)
            return JsonSerializer.Serialize(value, column.ClrType, Constants.JsonOptions);

        var type = column?.StorageType ?? Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();

        if (type.IsEnum)
            return column?.StoreEnumAsText == true ? value.ToString() : Convert.ToInt64(value, CultureInfo.InvariantCulture);

        return value switch {
            bool b => b ? 1 : 0,
            char c => c.ToString(),
            Guid guid => guid.ToString("D"),
            DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            TimeSpan timeSpan => timeSpan.Ticks,
            decimal number => Convert.ToDouble(number, CultureInfo.InvariantCulture),
            _ => value
        };
    }

    public static object? FromDatabase(object? value, DatabaseColumn column)
    {
        if (value is null || value is DBNull)
            return null;

        if (column.IsJson)
            return JsonSerializer.Deserialize(value.ToString()!, column.ClrType, Constants.JsonOptions);

        var target = column.StorageType;

        if (target.IsEnum)
        {
            if (column.StoreEnumAsText)
                return Enum.Parse(target, value.ToString()!, ignoreCase: true);

            return Enum.ToObject(target, Convert.ToInt64(value, CultureInfo.InvariantCulture));
        }

        if (target == typeof(string))
            return value.ToString();

        if (target == typeof(char))
            return value.ToString()![0];

        if (target == typeof(Guid))
            return Guid.Parse(value.ToString()!);

        if (target == typeof(DateTime))
            return DateTime.Parse(value.ToString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        if (target == typeof(DateTimeOffset))
            return DateTimeOffset.Parse(value.ToString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        if (target == typeof(TimeSpan))
            return TimeSpan.FromTicks(Convert.ToInt64(value, CultureInfo.InvariantCulture));

        if (target == typeof(bool))
            return Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0;

        if (target == typeof(byte[]))
            return (byte[])value;

        return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
    }
}
