using System.Text.Json.Serialization;
using Starlight.Common;

namespace Starlight.SDK.Http.Models;

/// <summary>
/// Wraps the full account info block the client expects only
/// after a successful credential login.
/// </summary>
public sealed class ShieldLoginResponse
{
    [JsonPropertyName("account")]
    public required ShieldAccountInfo Account { get; init; }

    [JsonPropertyName("real_person_required")]
    public bool RealPersonRequired { get; init; }

    [JsonPropertyName("safe_mobile_required")]
    public bool SafeMobileRequired { get; init; }

    [JsonPropertyName("reactivate_required")]
    public bool ReactivateRequired { get; init; }

    [JsonPropertyName("device_grant_required")]
    public bool DeviceGrantRequired { get; init; }

    /// <summary>
    /// Real-name flow marker. One of the constants on
    /// <see cref="RealNameOperations"/>. Defaults to
    /// <see cref="RealNameOperations.None"/>.
    /// </summary>
    [JsonPropertyName("real_name_operation")]
    public string RealNameOperation { get; init; } = RealNameOperations.None;
}

public sealed class ShieldAccountInfo
{
    [JsonPropertyName("id")]
    public required uint Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyName("token")]
    public required string Token { get; init; }

    // Fields below are present in the wire schema but unused by Starlight at
    // this stage
    // TODO: use them if needed later on
    [JsonPropertyName("mobile")]
    public string Mobile { get; init; } = string.Empty;

    [JsonPropertyName("is_email_verify")]
    public string IsEmailVerify { get; init; } = SdkDefaults.ZeroTimestamp;

    [JsonPropertyName("realname")]
    public string Realname { get; init; } = string.Empty;

    [JsonPropertyName("identity_card")]
    public string IdentityCard { get; init; } = string.Empty;

    [JsonPropertyName("facebook_name")]
    public string FacebookName { get; init; } = string.Empty;

    [JsonPropertyName("google_name")]
    public string GoogleName { get; init; } = string.Empty;

    [JsonPropertyName("twitter_name")]
    public string TwitterName { get; init; } = string.Empty;

    [JsonPropertyName("game_center_name")]
    public string GameCenterName { get; init; } = string.Empty;

    [JsonPropertyName("apple_name")]
    public string AppleName { get; init; } = string.Empty;

    [JsonPropertyName("sony_name")]
    public string SonyName { get; init; } = string.Empty;

    [JsonPropertyName("tap_name")]
    public string TapName { get; init; } = string.Empty;

    [JsonPropertyName("country")]
    public string Country { get; init; } = SdkDefaults.DefaultCountryCode;

    [JsonPropertyName("reactivate_ticket")]
    public string ReactivateTicket { get; init; } = string.Empty;

    [JsonPropertyName("area_code")]
    public string AreaCode { get; init; } = string.Empty;

    [JsonPropertyName("device_grant_ticket")]
    public string DeviceGrantTicket { get; init; } = string.Empty;

    [JsonPropertyName("steam_name")]
    public string SteamName { get; init; } = string.Empty;

    [JsonPropertyName("unmasked_email")]
    public string UnmaskedEmail { get; init; } = string.Empty;

    [JsonPropertyName("unmasked_email_type")]
    public string UnmaskedEmailType { get; init; } = SdkDefaults.ZeroTimestamp;

    [JsonPropertyName("cx_name")]
    public string CxName { get; init; } = string.Empty;
}
