using System.Text.Json.Serialization;

namespace Starlight.SDK;

/// <summary>
/// Per-app third-party login token configuration. Used as the value
/// type of <see cref="SdkShieldConfig.ThirdPartyConfigs"/>.
/// </summary>
public sealed class ThirdPartyTokenConfig
{
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = TokenKind.GameToken;

    /// <summary>
    /// Lifetime of the issued game token in seconds.
    /// </summary>
    [JsonPropertyName("game_token_expires_in")]
    public long GameTokenExpiresIn { get; set; } = TokenExpiry.ThirtyDays;
}

/// <summary>Well-known token-type markers used in <see cref="ThirdPartyTokenConfig.TokenType"/>.</summary>
public static class TokenKind
{
    /// <summary>
    /// Marker indicating the issued token is a game token (rather than
    /// an OAuth access token).
    /// </summary>
    public const string GameToken = "TK_GAME_TOKEN";
}

/// <summary>
/// Token-expiry constants used as default values for
/// <see cref="ThirdPartyTokenConfig.GameTokenExpiresIn"/>.
/// </summary>
public static class TokenExpiry
{
    /// <summary>Seven days, in seconds (604_800).</summary>
    public const long SevenDays = 7 * 24 * 60 * 60L;

    /// <summary>Thirty days, in seconds (2_592_000).</summary>
    public const long ThirtyDays = 30 * 24 * 60 * 60L;
}
