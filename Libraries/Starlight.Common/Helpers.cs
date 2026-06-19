using System.Net.Http.Headers;

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

public static class SystemHelper
{
    private const string EchoApi = "https://api.ipify.org/";

    private static readonly HttpClient Client = new();

    static SystemHelper()
    {
        Client.DefaultRequestHeaders.Add("User-Agent", "kitkat-multiverse/Starlight");
    }

    /// <summary>
    /// Fetches the system's public IP address, even if they're behind NAT.
    /// </summary>
    /// <returns>The IP address fetched from an echo API, or <code>127.0.0.1</code> if it fails.</returns>
    public static async Task<string> PublicIpAddress()
    {
        try
        {
            var result = await Client.GetStringAsync(EchoApi);
            return result.Trim();
        }
        catch
        {
            return "127.0.0.1";
        }
    }
}
