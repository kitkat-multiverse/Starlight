using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Starlight.SDK.Http.Models;

public sealed class StarlightPatchResponse
{
    [JsonPropertyName("sdkKey")]
    public string SdkKey { get; set; }

    [JsonPropertyName("checkSignKey")]
    public string CheckSignKey { get; set; }

    [JsonPropertyName("useSdkRsa")]
    public bool UseSdkRsa { get; set; }
}
