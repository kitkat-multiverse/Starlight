using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Starlight.Common;

namespace Starlight.SDK.Services;

/// <summary>
/// Real <see cref="IGeoIpLookup"/> backed by the ip-api.com JSON endpoint
/// (<a href="https://ip-api.com/docs/api:json">documentation</a>).
/// </summary>
/// <remarks>
/// <para>
/// The free tier of ip-api.com is HTTP-only, requires no API key, and is
/// rate-limited to 45 requests per minute per server IP. This
/// implementation does three things to stay safely under that limit:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     Short-circuits loopback / private / reserved addresses locally so
///     we never burn a request on a client that obviously cannot be
///     geolocated (e.g. <c>127.0.0.1</c>, <c>192.168.x.x</c>,
///     <c>::1</c>, ULA <c>fc00::/7</c>).
///     </description>
///   </item>
///   <item>
///     <description>
///     Caches successful lookups per IP for
///     <see cref="IpApiGeoIpConfig.CacheTtlSeconds"/> seconds so the
///     typical "user logs in, then exchanges the session token a few
///     seconds later" flow only consumes one ip-api.com request.
///     </description>
///   </item>
///   <item>
///     <description>
///     Honours the <c>X-Rl</c> (requests remaining) and <c>X-Ttl</c>
///     (seconds until the rate-limit window resets) response headers
///     exactly as the docs require: when <c>X-Rl</c> reaches 0 we stop
///     sending requests for <c>X-Ttl</c> seconds and fall back to
///     <see cref="SdkConfig.DefaultCountryCode"/> for every caller.
///     </description>
///   </item>
/// </list>
/// <para>
/// Any failure (timeout, non-2xx, malformed body, <c>status:"fail"</c>
/// from ip-api for a reserved range we missed) results in
/// <see cref="SdkConfig.DefaultCountryCode"/> being returned, the lookup
/// must never break the login flow.
/// </para>
/// </remarks>
public sealed class IpApiGeoIpLookup : IGeoIpLookup
{
    private readonly HttpClient _http;
    private readonly SdkConfig _sdkConfig;
    private readonly ILogger<IpApiGeoIpLookup> _logger;

    /// <summary>
    /// Per-IP cache of successful lookups. Keyed by the raw IP string the
    /// caller passed in (so we don't accidentally normalize
    /// <c>192.168.0.1</c> and <c>192.168.000.001</c> differently, both
    /// would be short-circuited anyway).
    /// </summary>
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    /// <summary>
    /// UTC ticks at which the current rate-limit window resets and we may
    /// start sending requests again. 0 means "no active throttle". Read
    /// and written via <see cref="Interlocked"/> because the lookup is
    /// invoked concurrently from every login request.
    /// </summary>
    private long _rateLimitResetTicks;

    public IpApiGeoIpLookup(
        HttpClient http,
        SdkConfig sdkConfig,
        ILogger<IpApiGeoIpLookup> logger
    )
    {
        _http = http;
        _sdkConfig = sdkConfig;
        _logger = logger;
    }

    public async Task<string> GetCountryCodeAsync(string? ipAddress, CancellationToken ct = default)
    {
        var fallback = FallbackCountry;

        if (string.IsNullOrWhiteSpace(ipAddress))
            return fallback;

        if (IsLocalOrPrivateAddress(ipAddress))
            return fallback;

        if (_sdkConfig.IpApi.CacheTtlSeconds > 0
            && _cache.TryGetValue(ipAddress, out var cached)
            && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.CountryCode;
        }

        // Respect the active rate-limit window.
        if (Interlocked.Read(ref _rateLimitResetTicks) > DateTimeOffset.UtcNow.Ticks)
        {
            _logger.LogDebug("ip-api.com rate-limit window still active, falling back to {Country}", fallback);
            return fallback;
        }

        try
        {
            var requestUrl = BuildRequestUrl(ipAddress);
            using var response = await _http.GetAsync(requestUrl, ct).ConfigureAwait(false);

            UpdateRateLimitWindow(response);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("ip-api.com returned 429 Too Many Requests, backing off");
                return fallback;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ip-api.com returned HTTP {Status}", response.StatusCode);
                return fallback;
            }

            var body = await response.Content
                .ReadFromJsonAsync<IpApiResponse>(cancellationToken: ct)
                .ConfigureAwait(false);

            if (body is null || !body.Success)
            {
                _logger.LogDebug("ip-api.com lookup for {Ip} failed: {Message}",
                    ipAddress, body?.Message ?? "null body");
                return fallback;
            }

            var countryCode = body.CountryCode ?? string.Empty;

            if (string.IsNullOrWhiteSpace(countryCode))
                return fallback;

            if (_sdkConfig.IpApi.CacheTtlSeconds > 0)
            {
                _cache[ipAddress] = new CacheEntry(
                    countryCode,
                    DateTimeOffset.UtcNow.AddSeconds(_sdkConfig.IpApi.CacheTtlSeconds));
            }

            return countryCode;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ip-api.com lookup for {Ip} threw, falling back to {Country}",
                ipAddress, fallback);
            return fallback;
        }
    }

    private string FallbackCountry =>
        string.IsNullOrWhiteSpace(_sdkConfig.DefaultCountryCode) ? "US" : _sdkConfig.DefaultCountryCode;

    private string BuildRequestUrl(string ipAddress)
    {
        var fields = "status,message,countryCode";

        var query = $"?fields={fields}&lang={Uri.EscapeDataString(_sdkConfig.IpApi.Lang)}";

        if (!string.IsNullOrWhiteSpace(_sdkConfig.IpApi.ApiKey))
            query += $"&key={Uri.EscapeDataString(_sdkConfig.IpApi.ApiKey!)}";

        return $"{Uri.EscapeDataString(ipAddress)}{query}";
    }

    /// <summary>
    /// Reads the <c>X-Rl</c> (requests remaining) and <c>X-Ttl</c>
    /// (seconds until reset) headers from the response and, if the
    /// remaining count has hit zero, marks the lookup as throttled until
    /// the reset time. Per the ip-api.com docs, callers MUST NOT send
    /// further requests while the throttle is active.
    /// </summary>
    private void UpdateRateLimitWindow(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-Rl", out var rlValues))
            return;

        if (!int.TryParse(rlValues.FirstOrDefault(), out var remaining) || remaining > 0)
            return;

        if (!response.Headers.TryGetValues("X-Ttl", out var ttlValues))
            return;

        if (!int.TryParse(ttlValues.FirstOrDefault(), out var ttlSeconds) || ttlSeconds <= 0)
            return;

        var resetAt = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds).Ticks;
        Interlocked.Exchange(ref _rateLimitResetTicks, resetAt);

        _logger.LogInformation("ip-api.com rate limit reached, backing off for {Ttl}s", ttlSeconds);
    }

    private static bool IsLocalOrPrivateAddress(string ipAddress)
    {
        if (!IPAddress.TryParse(ipAddress, out var addr))
            return true;

        if (IPAddress.IsLoopback(addr))
            return true;

        if (addr.IsIPv4MappedToIPv6)
            addr = addr.MapToIPv4();

        switch (addr.AddressFamily)
        {
            case AddressFamily.InterNetwork: {
                var b = addr.GetAddressBytes();
                // 10.0.0.0/8
                if (b[0] == 10) return true;

                // 172.16.0.0/12
                if (b[0] == 172 && (b[1] & 0xF0) == 16) return true;

                // 192.168.0.0/16
                if (b[0] == 192 && b[1] == 168) return true;

                // 169.254.0.0/16 (link-local)
                if (b[0] == 169 && b[1] == 254) return true;

                // 100.64.0.0/10 (CGNAT)
                if (b[0] == 100 && (b[1] & 0xC0) == 64) return true;

                break;
            }
            case AddressFamily.InterNetworkV6: {
                if (addr.IsIPv6LinkLocal) return true;
                if (addr.IsIPv6SiteLocal) return true;

                var b = addr.GetAddressBytes();
                // Unique local addresses fc00::/7
                if ((b[0] & 0xFE) == 0xFC) return true;

                break;
            }
        }

        return false;
    }

    private readonly record struct CacheEntry(string CountryCode, DateTimeOffset ExpiresAt);

    private sealed class IpApiResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("countryCode")]
        public string? CountryCode { get; set; }

        public bool Success => string.Equals(Status, "success", StringComparison.OrdinalIgnoreCase);
    }
}
