namespace Starlight.SDK;

/// <summary>
/// HTTP-style status codes used as numeric markers inside SDK response
/// payloads. These appear as inline literals in
/// <see cref="SdkComboBoxConfig.NetworkStatusCodes"/> defaults and in
/// the device-fingerprint endpoint's response code.
/// </summary>
public static class HttpStatus
{
    /// <summary>OK.</summary>
    public const int Ok = 200;

    /// <summary>Forbidden.</summary>
    public const int Forbidden = 403;

    /// <summary>Not Found.</summary>
    public const int NotFound = 404;

    /// <summary>Too Many Requests.</summary>
    public const int TooManyRequests = 429;
}

public static class SdkDefaults
{
    /// <summary>
    /// ISO-3166-1 alpha-2 country code returned to the client when
    /// GeoIP lookup is unavailable or disabled.
    /// </summary>
    public const string DefaultCountryCode = "US";

    /// <summary>Response language for the ma-passport <c>getConfig</c> endpoint.</summary>
    public const string MaPassportLanguage = "en-us";

    /// <summary>
    /// Guardian age limit reported by ma-passport. Upstream uses
    /// <c>"14"</c> for the overseas deployment.
    /// </summary>
    public const string GuardianAgeLimit = "14";

    /// <summary>
    /// Default <c>mid</c> returned in <c>user_info</c> blocks. Upstream
    /// shape; Starlight doesn't currently persist a separate mid.
    /// </summary>
    public const string DefaultMid = "18w9wecbdl_hy";

    /// <summary>API log level reported by the combo-granter <c>getConfig</c> endpoint.</summary>
    public const string ApiLogLevel = "DEBUG";

    /// <summary>Identity string reported by the shield <c>loadConfig</c> endpoint.</summary>
    public const string ShieldIdentity = "I_IDENTITY";

    /// <summary>Default scene reported by the shield <c>loadConfig</c> endpoint for non-PC platforms.</summary>
    public const string ShieldSceneNormal = "S_NORMAL";

    /// <summary>Scene reported by the shield <c>loadConfig</c> endpoint for the PC platform.</summary>
    public const string ShieldSceneAccount = "S_ACCOUNT";

    /// <summary>Comma-separated list of client versions the shield endpoint ignores for compatibility checks.</summary>
    public const string ShieldIgnoreVersions = "2.6.0";

    /// <summary>
    /// Value reported in <c>combo_id</c> on the combo-granter login
    /// response. Defaults to <c>"0"</c>.
    /// </summary>
    public const string DefaultComboId = "0";

    /// <summary>
    /// Sentinel used wherever the upstream API expects a Unix timestamp
    /// string but Starlight doesn't have a real value to send (e.g.
    /// <c>password_time</c> on a freshly auto-created account).
    /// </summary>
    public const string ZeroTimestamp = "0";
}

/// <summary>
/// Wire-format markers for the real-name verification flow. Stored on
/// <see cref="Database.Models.Account.RealNameOperation"/> and returned
/// in the <c>realname_operation</c> field of the shield login and
/// ma-passport login responses.
/// </summary>
public static class RealNameOperations
{
    /// <summary>
    /// No real-name flow is pending for the account. Default value
    /// reported when the account has already completed verification or
    /// has never been flagged.
    /// </summary>
    public const string None = "None";

    /// <summary>
    /// Account must still complete real-name verification. Reported in
    /// the response of <c>shield/login</c> and
    /// <c>ma-passport/appLoginByPassword</c> when
    /// <see cref="Database.Models.Account.RequireRealPerson"/> is set.
    /// </summary>
    public const string BindRealname = "bindRealname";
}

/// <summary>
/// Short codes used as dictionary keys in
/// <see cref="SdkShieldConfig.ThirdPartyConfigs"/> and as entries in
/// <see cref="SdkShieldConfig.ThirdPartyApps"/>.
/// </summary>
public static class ThirdPartyApp
{
    /// <summary>Apple Sign-in.</summary>
    public const string Apple = "ap";

    /// <summary>Google Sign-in.</summary>
    public const string Google = "gl";

    /// <summary>Facebook Login.</summary>
    public const string Facebook = "fb";

    /// <summary>Twitter / X Login.</summary>
    public const string Twitter = "tw";

    /// <summary>Game Center (iOS) Sign-in.</summary>
    public const string GameCenter = "gc";

    /// <summary>TapTap Sign-in.</summary>
    public const string TapTap = "tp";
}

public enum MaPassportTokenType
{
    /// <summary>
    /// Long-lived server token (stoken). Issued by
    /// <c>appLoginByAuthTicket</c> and <c>verifySToken</c>.
    /// </summary>
    Stoken = 1,

    /// <summary>
    /// Short-lived game / session token. Issued by
    /// <c>appLoginByPassword</c> and <c>reactivateAccount</c>.
    /// </summary>
    GameToken = 3
}
