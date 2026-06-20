using System.Text.Json.Serialization;

namespace Starlight.SDK.Http.Models;

public sealed class ShieldLoginRequest
{
    [JsonPropertyName("account")]
    public string? Account { get; set; }

    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("is_crypto")]
    public bool? IsCrypto { get; set; }

    [JsonPropertyName("game_key")]
    public string? GameKey { get; set; }
}
