namespace Starlight.Common;

public static class SystemHelper
{
    private const string EchoApi = "https://api.ipify.org/";

    private static readonly HttpClient Client = new();

    private static string? _ipAddress;

    static SystemHelper()
    {
        Client.Timeout = TimeSpan.FromSeconds(5);
        Client.DefaultRequestHeaders.Add("User-Agent", "kitkat-multiverse/Starlight");
    }

    /// <summary>
    /// Fetches the system's public IP address, even if they're behind NAT.
    /// </summary>
    /// <returns>The IP address fetched from an echo API.</returns>
    /// <exception cref="HttpRequestException">Thrown when IP discovery fails.</exception>
    public static async Task<string> PublicIpAddress(CancellationToken ct = default)
    {
        if (_ipAddress is not null) return _ipAddress;

        var result = await Client.GetStringAsync(EchoApi, ct);
        return _ipAddress = result.Trim();
    }
}
