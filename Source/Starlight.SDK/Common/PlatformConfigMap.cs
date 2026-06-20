namespace Starlight.SDK.Common;

/// <summary>
/// Static lookup table from <see cref="PlatformId"/> to the configuration
/// id reported by <c>/hk4e_global/mdk/shield/api/loadConfig</c> in the
/// <c>id</c> field of <see cref="Starlight.SDK.Http.Models.ShieldLoadConfigData"/>.
/// </summary>
/// <remarks>
/// The mapping was previously an inline <c>switch</c> expression in
/// <c>ShieldEndpoints.GetConfigId</c>; it lives here so the magic numbers
/// are named in exactly one place. Unknown platforms resolve to
/// <see cref="Unknown"/>.
/// </remarks>
public static class PlatformConfigMap
{
    /// <summary>
    /// Sentinel returned for platform ids the official client never
    /// sends. Matches the previous inline <c>-1</c> default.
    /// </summary>
    public const int Unknown = -1;

    /// <summary>
    /// Returns the configuration id reported to the client for the given
    /// <paramref name="platform"/>, or <see cref="Unknown"/> if the
    /// platform is not in the table.
    /// </summary>
    public static int GetConfigId(PlatformId platform) => platform switch {
        PlatformId.Ios => 4,
        PlatformId.Android => 5,
        PlatformId.Pc => 6,
        PlatformId.PlayStation => 30,
        PlatformId.CloudAndroid => 27,
        PlatformId.CloudPc => 53,
        PlatformId.CloudIos => 26,
        PlatformId.PlayStation5 => 28,
        PlatformId.CloudMacOs => 44,
        _ => Unknown
    };

    /// <summary>
    /// Returns the human-readable platform name reported to the client
    /// for the given <paramref name="platform"/>, or an empty string for
    /// unknown ids.
    /// </summary>
    public static string GetPlatformName(PlatformId platform) => platform switch {
        PlatformId.Ios => "IOS",
        PlatformId.Android => "Android",
        PlatformId.Pc => "PC",
        PlatformId.BrowserA or PlatformId.BrowserB => "Browser",
        PlatformId.PlayStation => "PS",
        PlatformId.CloudAndroid => "CloudAndroid",
        PlatformId.CloudPc => "CloudPC",
        PlatformId.CloudIos => "CloudIOS",
        PlatformId.PlayStation5 => "PS5",
        PlatformId.MacOS or PlatformId.MacOSAlt => "MacOS",
        PlatformId.CloudMacOs => "CloudMacOS",
        _ => string.Empty
    };
}
