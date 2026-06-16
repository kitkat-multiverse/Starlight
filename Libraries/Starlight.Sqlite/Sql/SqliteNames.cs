namespace Starlight.Database.Sql;

internal static class SqliteNames
{
    public static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("SQLite identifiers cannot be empty.", nameof(identifier));

        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }
}
