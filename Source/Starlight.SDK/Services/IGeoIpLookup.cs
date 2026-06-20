namespace Starlight.SDK.Services;

public interface IGeoIpLookup
{
    /// <summary>
    /// Returns the country code for <paramref name="ipAddress"/> or the
    /// configured default when the lookup fails or is disabled. Must never
    /// throw.
    /// </summary>
    Task<string> GetCountryCodeAsync(string? ipAddress, CancellationToken ct = default);
}
