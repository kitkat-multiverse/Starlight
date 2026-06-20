using System.Text.Json.Serialization;
using Starlight.Common;
using Starlight.SDK.Common;

namespace Starlight.SDK.Http.Models;

public sealed class ComboGranterLoginResponse
{
    [JsonPropertyName("combo_id")]
    public required string ComboId { get; init; }

    [JsonPropertyName("open_id")]
    public required string OpenId { get; init; }

    [JsonPropertyName("combo_token")]
    public required string ComboToken { get; init; }

    [JsonPropertyName("data")]
    public required string Data { get; init; } // JSON-string blob, see ComboInnerData

    [JsonPropertyName("heartbeat")]
    public bool Heartbeat { get; init; }

    [JsonPropertyName("account_type")]
    public AccountType AccountType { get; init; } = AccountType.Normal;

    [JsonPropertyName("fatigue_remind")]
    public string? FatigueRemind { get; init; }
}

/// <summary>
/// Payload nested inside <see cref="ComboGranterLoginResponse.Data"/> as a
/// JSON string.
/// </summary>
public sealed class ComboInnerData
{
    [JsonPropertyName("guest")]
    public bool Guest { get; init; }

    [JsonPropertyName("country_code")]
    public string CountryCode { get; init; } = SdkDefaults.DefaultCountryCode;

    [JsonPropertyName("is_new_register")]
    public bool IsNewRegister { get; init; }
}
