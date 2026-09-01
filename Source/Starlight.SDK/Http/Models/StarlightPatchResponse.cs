using System.Text.Json.Serialization;

namespace Starlight.SDK.Http.Models;

public sealed class StarlightPatchResponse
{
    [JsonPropertyName("sdkKey")]
    public string SdkKey { get; set; } = string.Empty;

    [JsonPropertyName("checkSignKey")]
    public string CheckSignKey { get; set; } = string.Empty;

    [JsonPropertyName("useSdkRsa")]
    public bool UseSdkRsa { get; set; }
}
