using System.Net.Mime;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;

namespace Starlight.SDK.Http;

/// <summary>
/// Helpers shared by the SDK HTTP endpoints: random-token generation,
/// client-IP extraction (honouring reverse-proxy forwarded headers),
/// string masking for PII fields, country-code -> mobile-dialling-code
/// mapping, and content-type resolution for the webstatic file server.
/// </summary>
/// <remarks>
/// These were previously duplicated as private methods on
/// <see cref="Endpoints.PassportEndpoints"/>,
/// <see cref="Endpoints.ShieldEndpoints"/> and
/// <see cref="Services.AuthService"/>. They are aggregated here so the
/// behaviour stays consistent across endpoints and any future tweak
/// (e.g. a new forwarded header, a different mask width) only has to
/// be made in one place.
/// </remarks>
public static class SdkHttpHelpers
{
    /// <summary>
    /// Generates a cryptographically random alphanumeric token of the
    /// given length using the same alphabet as the upstream SDK
    /// (<c>A-Z a-z 0-9</c>).
    /// </summary>
    public static string GenerateToken(int length)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        Span<char> buffer = stackalloc char[length];

        for (var i = 0; i < length; i++)
            buffer[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

        return new string(buffer);
    }

    /// <summary>
    /// Resolves the originating client IP from the request, honouring
    /// <c>X-Forwarded-For</c> and <c>X-Real-IP</c> when set (typical when
    /// running behind a reverse proxy). Returns <c>null</c> if no IP can
    /// be determined.
    /// </summary>
    public static string? GetClientIp(HttpContext httpContext)
    {
        var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].ToString();

        if (!string.IsNullOrWhiteSpace(forwardedFor))
            return forwardedFor.Split(',')[0].Trim();

        var realIp = httpContext.Request.Headers["X-Real-IP"].ToString();

        if (!string.IsNullOrWhiteSpace(realIp))
            return realIp.Trim();

        return httpContext.Connection.RemoteIpAddress?.ToString();
    }

    /// <summary>
    /// Masks all but the first/last few characters of a string, suitable
    /// for returning masked emails/phones in user_info blocks. Returns an
    /// empty string for null/empty input; returns an all-stars string for
    /// very short input.
    /// </summary>
    public static string MaskString(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        if (text.Length < 4)
            return new string(c: '*', text.Length);

        var start = text.Length >= 10 ? 2 : 1;
        var end = text.Length > 5 ? 2 : 1;
        var masked = new string(c: '*', text.Length - start - end);
        return string.Concat(text.AsSpan(start: 0, start), masked, text.AsSpan(text.Length - end));
    }

    /// <summary>
    /// Maps an ISO-3166-1 alpha-2 country code to its ITU mobile dialling
    /// code (without the leading +). Used by the ma-passport config
    /// endpoint to populate <c>area_code</c> for the SDK's phone-input
    /// flow. Returns an empty string for unknown countries.
    /// </summary>
    public static string CountryToMobileCode(string countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
            return string.Empty;

        return countryCode.ToUpperInvariant() switch {
            "US" => "1",
            "CA" => "1",
            "GB" => "44",
            "FR" => "33",
            "DE" => "49",
            "JP" => "81",
            "KR" => "82",
            "CN" => "86",
            "TW" => "886",
            "HK" => "852",
            "SG" => "65",
            "IN" => "91",
            "BR" => "55",
            "RU" => "7",
            "AU" => "61",
            "NZ" => "64",
            "IT" => "39",
            "ES" => "34",
            "PT" => "351",
            "MX" => "52",
            "ID" => "62",
            "TH" => "66",
            "VN" => "84",
            "PH" => "63",
            "MY" => "60",
            "SA" => "966",
            "AE" => "971",
            "TR" => "90",
            "NL" => "31",
            "BE" => "32",
            "CH" => "41",
            "AT" => "43",
            "SE" => "46",
            "NO" => "47",
            "DK" => "45",
            "FI" => "358",
            "PL" => "48",
            "UA" => "380",
            "EG" => "20",
            "ZA" => "27",
            _ => string.Empty
        };
    }

    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    /// <summary>
    /// Resolves a content-type string for a file path. Uses the built-in
    /// <see cref="FileExtensionContentTypeProvider"/> so we don't have to
    /// maintain our own extension table; falls back to
    /// <see cref="MediaTypeNames.Application.Octet"/> for unknown
    /// extensions.
    /// </summary>
    public static string GetContentType(string path)
    {
        if (!ContentTypeProvider.TryGetContentType(path, out var contentType))
            return MediaTypeNames.Application.Octet;

        return contentType;
    }
}
