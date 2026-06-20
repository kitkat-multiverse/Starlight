using Starlight.Common;

namespace Starlight.SDK.Services;

public sealed class DefaultGeoIpLookup(SdkConfig sdkConfig) : IGeoIpLookup
{
    private readonly string _defaultCountryCode =
        string.IsNullOrWhiteSpace(sdkConfig.DefaultCountryCode) ? "US" : sdkConfig.DefaultCountryCode;

    public Task<string> GetCountryCodeAsync(string? ipAddress, CancellationToken ct = default)
        => Task.FromResult(_defaultCountryCode);
}
