using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Starlight.SDK.Proto;

namespace Starlight.SDK;

/// <summary>
/// Configuration for the dispatch endpoints that provide the game region list
/// and the selected region's connection metadata.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public sealed class DispatchConfig
{
    /// <summary>
    /// Public base URL used to build region dispatch URLs. Leave empty to derive
    /// the URL from the incoming request Host/Scheme, including common
    /// X-Forwarded-* reverse-proxy headers.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// Whether the PC login flow is enabled in <c>QueryRegionListHttpRsp</c>.
    /// </summary>
    public bool EnableLoginPc { get; set; } = true;

    /// <summary>
    /// Default name applied when the region list is empty or a region omits a
    /// name. Region names must be unique because they are used in the
    /// <c>/query_cur_region/{name}</c> route.
    /// </summary>
    public string FallbackRegionName { get; set; } = "sl_local";

    /// <summary>
    /// Default region type surfaced in <see cref="RegionSimpleInfo.Type"/>.
    /// </summary>
    public string RegionType { get; set; } = "DEV_PUBLIC";

    /// <summary>
    /// Configured game regions returned by <c>/query_region_list</c>.
    /// </summary>
    public List<DispatchRegionConfig> Regions { get; set; } = [];

    /// <summary>
    /// Length, in bytes, of the random seed used to generate the process-local
    /// EC2B client secret at startup. The EC2B payload itself is always 2076
    /// bytes, and the matching 4096-byte xorpad is derived and cached in memory.
    /// No dispatch key material is read from or written to disk.
    /// </summary>
    public int GeneratedEc2bSeedLength { get; set; } = 32;

    /// <summary>
    /// Filesystem path to the PKCS#8 RSA private key used for signing
    /// the region payload.
    /// <br/>
    /// Leave empty to use a hardcoded signature instead.
    /// </summary>
    public string? RsaSigningKeyPath { get; set; } = "";

    /// <summary>
    /// JSON custom config embedded into <c>client_custom_config_encrypted</c>.
    /// </summary>
    public DispatchClientCustomConfig ClientCustomConfig { get; set; } = new();
}

/// <summary>
/// One game region advertised by the dispatch service.
/// </summary>
[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
public sealed class DispatchRegionConfig
{
    public string Name { get; set; } = "sl_local";
    public string Title { get; set; } = "Starlight";
    public string? Type { get; set; }
    public string? DispatchUrl { get; set; }

    public bool UseGateserverDomainName { get; set; }
    public string? GateserverDomainName { get; set; } = "";
    public string GameBiz { get; set; } = "hk4e_global";

    public string? PayCallbackUrl { get; set; }
    public string? AreaType { get; set; }
    public string? ResourceUrl { get; set; }
    public string? DataUrl { get; set; }
    public string? FeedbackUrl { get; set; }
    public string? BulletinUrl { get; set; }
    public string? ResourceUrlBak { get; set; }
    public string? DataUrlBak { get; set; }
    public uint ClientDataVersion { get; set; }
    public string? HandbookUrl { get; set; }
    public uint ClientSilenceDataVersion { get; set; }
    public string? ClientDataMd5 { get; set; }
    public string? ClientSilenceDataMd5 { get; set; }
    public string? OfficialCommunityUrl { get; set; }
    public string? ClientVersionSuffix { get; set; }
    public string? ClientSilenceVersionSuffix { get; set; }
    public string? UserCenterUrl { get; set; }
    public string? AccountBindUrl { get; set; }
    public string? CdkeyUrl { get; set; }
    public string? PrivacyPolicyUrl { get; set; }
    public string? NextResourceUrl { get; set; }
}

/// <summary>
/// Custom config object serialized into the dispatch region-list response.
/// Property names intentionally match the client's lower-case wire keys.
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public sealed class DispatchClientCustomConfig
{
    [JsonPropertyName("sdkenv")]
    public string SdkEnv { get; set; } = "2";

    [JsonPropertyName("checkdevice")]
    public string CheckDevice { get; set; } = "false";

    [JsonPropertyName("loadPatch")]
    public string LoadPatch { get; set; } = "false";

    [JsonPropertyName("showexception")]
    public bool ShowException { get; set; } = true;

    [JsonPropertyName("regionConfig")]
    public string RegionConfig { get; set; } = "pm|fk|add";

    [JsonPropertyName("downloadMode")]
    public string DownloadMode { get; set; } = "0";

    [JsonPropertyName("codeSwitch")]
    public List<int> CodeSwitch { get; set; } = [];

    [JsonPropertyName("coverSwitch")]
    public List<int> CoverSwitch { get; set; } = [];

    [JsonPropertyName("debugmenu")]
    public string DebugMenu { get; set; } = "true";

    [JsonPropertyName("debuglog")]
    public string DebugLog { get; set; } = "false";
}
