namespace Starlight.SDK.Common;

/// <summary>
/// Wire-format string keys used in the
/// <see cref="Starlight.SDK.Http.Models.ComboGranterConfigData.FunctionalSwitchConfigs"/>
/// dictionary returned by <c>/hk4e_global/combo/granter/api/getConfig</c>.
/// </summary>
/// <remarks>
/// These were previously inline string literals in
/// <c>ComboGranterEndpoints.HandleGetConfig</c>; grouping them here lets
/// the compiler catch typos that would otherwise silently produce a
/// misnamed config key the client SDK would ignore.
/// </remarks>
public static class FunctionalSwitchKey
{
    /// <summary>JPush push-notification SDK switch (iOS / Android).</summary>
    public const string JPush = "jpush";

    /// <summary>AppsFlyer attribution SDK init switch (iOS / Android / Cloud Android).</summary>
    public const string InitializeAppsFlyer = "initialize_appsflyer";

    /// <summary>Android-only notification permission prompt switch.</summary>
    public const string AllowNotification = "allow_notification";
}
