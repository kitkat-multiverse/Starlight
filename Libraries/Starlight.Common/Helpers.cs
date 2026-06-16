namespace Starlight.Common;

public enum ProviderType
{
    Sqlite
}

public static class DatabaseHelper
{
    /// <summary>
    /// Parses a connection string into a provider, and extrapolates the part needed
    /// for the connection.
    /// </summary>
    public static ProviderType? ParseProvider(string path, out string stripped)
    {
        stripped = path;

        if (path.StartsWith("sqlite:"))
        {
            stripped = path[7..].Trim();
            return ProviderType.Sqlite;
        }

        return null;
    }
}
