namespace Starlight.SDK.Common;

/// <summary>
/// Canonical client platform identifiers used by every SDK endpoint that
/// accepts a <c>client_type</c> / <c>platform</c> / <c>client</c> query
/// parameter. The wire format is the integer value (1..13).
/// </summary>
/// <remarks>
/// Values are sent on the wire as integers, so the default
/// <see cref="System.Text.Json.Serialization"/> behaviour serializes the
/// enum numerically and existing clients remain compatible.
/// </remarks>
public enum PlatformId
{
    /// <summary>iOS native client.</summary>
    Ios = 1,

    /// <summary>Android native client.</summary>
    Android = 2,

    /// <summary>Windows / macOS PC client.</summary>
    Pc = 3,

    /// <summary>Browser variant A.</summary>
    BrowserA = 4,

    /// <summary>Browser variant B.</summary>
    BrowserB = 5,

    /// <summary>PlayStation 4.</summary>
    PlayStation = 6,

    /// <summary>macOS native client (unused by combo box config).</summary>
    MacOS = 7,

    /// <summary>Cloud Android streaming client.</summary>
    CloudAndroid = 8,

    /// <summary>Cloud PC streaming client.</summary>
    CloudPc = 9,

    /// <summary>Cloud iOS streaming client.</summary>
    CloudIos = 10,

    /// <summary>PlayStation 5.</summary>
    PlayStation5 = 11,

    /// <summary>macOS native client (alternate id, unused by combo box config).</summary>
    MacOSAlt = 12,

    /// <summary>Cloud macOS streaming client.</summary>
    CloudMacOs = 13
}

/// <summary>
/// Convenience helpers for <see cref="PlatformId"/>. Grouped here so the
/// switch expressions live in one place rather than being re-derived in
/// every endpoint that needs to know "is this a phone?" or "is this a
/// console?".
/// </summary>
public static class PlatformIdExtensions
{
    /// <summary>
    /// Mobile phone platforms (iOS / Android / Cloud Android). Used by the
    /// shield loadConfig endpoint to decide whether to enable
    /// Firebase-related switches.
    /// </summary>
    public static bool IsPhone(this PlatformId platform) =>
        platform is PlatformId.Ios or PlatformId.Android or PlatformId.CloudAndroid;

    /// <summary>
    /// PC-style platforms (PC / Cloud PC). Used by the combo granter
    /// getConfig endpoint to decide whether to populate
    /// <c>qr_enabled_apps</c> / <c>qr_app_icons</c>.
    /// </summary>
    public static bool IsPcLike(this PlatformId platform) =>
        platform is PlatformId.Pc or PlatformId.CloudPc;

    /// <summary>
    /// Console platforms (PS / PS5 / Cloud iOS / Cloud macOS). Used by the
    /// combo box endpoint to gate telemetry-only config blocks.
    /// </summary>
    public static bool IsConsole(this PlatformId platform) =>
        platform is PlatformId.PlayStation
            or PlatformId.PlayStation5;

    /// <summary>
    /// Platforms that the combo box config endpoint rejects. Currently
    /// the two macOS variants because the client does not ship a config
    /// for them.
    /// </summary>
    public static bool IsUnsupportedByComboBox(this PlatformId platform) =>
        platform is PlatformId.MacOS or PlatformId.MacOSAlt;
}
