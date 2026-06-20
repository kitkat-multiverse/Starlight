using Starlight.Database;

namespace Starlight.SDK;

/// <summary>
/// Configuration for the SDK HTTP server: bind address/port, HMAC key,
/// RSA password-decryption key path, account auto-create policy, and
/// the per-endpoint config blobs returned by the shield / combo-granter /
/// combo-box / ma-passport / abtest / device-fp endpoints.
/// </summary>
public sealed class SdkConfig
{
    /// HTTP server bind address.
    /// <br/>
    /// Use <code>0.0.0.0</code> to bind on all addresses.
    public string BindAddress { get; set; } = "0.0.0.0";
    /// HTTP server bind port.
    public int BindPort { get; set; } = 8080;

    /// <summary>
    /// Shared HMAC-SHA256 key used to verify <c>sign</c> on the combo
    /// granter login endpoint. Must match the value the client was built
    /// with.
    /// </summary>
    public string HmacKey { get; set; } = string.Empty;

    /// <summary>
    /// When true, the combo granter endpoint accepts requests without
    /// validating their HMAC signature. Intended for local development
    /// only, never enable in production.
    /// </summary>
    public bool SkipSignatureCheck { get; set; }

    /// <summary>
    /// When true, skip decrypting the client's password when logging in.
    /// <br/>
    /// Useful for when the client does not have RSA patched with the server's matching key.
    /// </summary>
    public bool SkipRsaDecryption { get; set; }

    /// <summary>
    /// Filesystem path to the PKCS#8 RSA private key the shield login
    /// endpoint uses to decrypt passwords sent with <c>is_crypto=true</c>.
    /// Leave empty to disable RSA password decryption.
    /// </summary>
    public string? PasswordRsaKeyPath { get; set; } = "";

    /// <summary>
    /// When true, a login attempt for a username that doesn't exist yet
    /// will create a brand-new account using the supplied credentials
    /// instead of failing with <see cref="Common.Retcode.LoginInvalidAccount"/>.
    /// When false (the default), unknown accounts are rejected and must be
    /// created.
    /// </summary>
    public bool AllowAccountAutoCreate { get; set; }

    /// <summary>
    /// ISO-3166 country code returned to the client when GeoIP lookup is
    /// unavailable or disabled. Defaults to
    /// <see cref="SdkDefaults.DefaultCountryCode"/>.
    /// </summary>
    public string DefaultCountryCode { get; set; } = SdkDefaults.DefaultCountryCode;

    /// <summary>
    /// Value reported in <c>realname_operation</c> when no real-name flow
    /// is pending for the account. Defaults to
    /// <see cref="RealNameOperations.None"/>.
    /// </summary>
    public string DefaultRealNameOperation { get; set; } = RealNameOperations.None;

    /// <summary>
    /// Value reported in <c>combo_id</c> on the combo granter login
    /// response. Defaults to <see cref="SdkDefaults.DefaultComboId"/>.
    /// Still go no idea what "ComboID" is really used for maybe hiro could know
    /// </summary>
    public string DefaultComboId { get; set; } = SdkDefaults.DefaultComboId;

    /// <summary>
    /// Minimum accepted length of a (decrypted) password.
    /// </summary>
    public int MinPasswordLength { get; set; } = 8;

    /// <summary>
    /// Configuration for the database. Defaults to SQLite.
    /// </summary>
    public DatabaseConfig Database { get; set; } = new();

    /// <summary>
    /// Configuration for the real ip-api.com GeoIP lookup. When
    /// <see cref="IpApiGeoIpConfig.Enabled"/> is <c>true</c> the SDK
    /// registers <see cref="Services.IpApiGeoIpLookup"/>
    /// as the <see cref="Services.IGeoIpLookup"/> implementation;
    /// otherwise the no-op <see cref="Services.DefaultGeoIpLookup"/>
    /// is used and every request resolves to
    /// <see cref="DefaultCountryCode"/>.
    /// </summary>
    public IpApiGeoIpConfig IpApi { get; set; } = new();

    /// <summary>
    /// Configuration for the static webstatic endpoints
    /// (<c>/admin/mi18n/**</c>, <c>/webstatic/**</c>, ...). The controller
    /// answers known <c>*-version.json</c> paths with a small in-memory
    /// version map; arbitrary files are served from
    /// <see cref="WebstaticConfig.ResourceRoot"/> when set.
    /// </summary>
    public WebstaticConfig Webstatic { get; set; } = new();

    /// <summary>
    /// Configuration returned by <c>/hk4e_global/mdk/shield/api/loadConfig</c>
    /// and <c>/hk4e_global/combo/granter/api/getConfig</c>. These endpoints
    /// drive most of the SDK's runtime feature flags on the client.
    /// </summary>
    public SdkShieldConfig Shield { get; set; } = new();

    /// <summary>
    /// Configuration returned by the <c>/combo/box/api/config/**</c>
    /// family of endpoints.
    /// </summary>
    public SdkComboBoxConfig ComboBox { get; set; } = new();

    /// <summary>
    /// Configuration returned by
    /// <c>/hk4e_global/account/ma-passport/api/getConfig</c>.
    /// </summary>
    public SdkMaPassportConfig MaPassport { get; set; } = new();

    /// <summary>
    /// Configuration returned by
    /// <c>/data_abtest_api/config/experiment/list</c>. Starlight does not
    /// persist experiments, so the response is built from this static
    /// list filtered by the requested scene id.
    /// </summary>
    public SdkAbTestConfig AbTest { get; set; } = new();

    /// <summary>
    /// Configuration returned by
    /// <c>/device-fp/api/getExtList</c>. Starlight does not persist device
    /// extension lists; the response is built from this static table.
    /// </summary>
    public SdkDeviceFpConfig DeviceFp { get; set; } = new();
}

public enum ProviderType
{
    Sqlite
}

public sealed class DatabaseConfig
{
    public ProviderType Provider { get; set; } = ProviderType.Sqlite;
    public StarlightDatabaseOptions Sqlite { get; set; } = new();
}

/// <summary>
/// Configuration for the ip-api.com JSON endpoint
/// (<a href="https://ip-api.com/docs/api:json">docs</a>). The free tier
/// is HTTP-only, requires no API key, and is rate-limited to 45 requests
/// per minute per server IP. The lookup implementation caches results
/// client-side and respects the <c>X-Rl</c> / <c>X-Ttl</c> rate-limit
/// headers returned by the service.
/// </summary>
public sealed class IpApiGeoIpConfig
{
    /// <summary>
    /// When <c>true</c>, the SDK calls ip-api.com to resolve client IPs
    /// to country codes. When <c>false</c> (default), the no-op
    /// <see cref="Services.DefaultGeoIpLookup"/> is used
    /// and every request resolves to
    /// <see cref="SdkConfig.DefaultCountryCode"/>.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Base URL of the ip-api.com JSON endpoint. The free tier only
    /// serves HTTP; if you have a pro account with HTTPS access, change
    /// this to <c>https://pro.ip-api.com/json</c> (and set
    /// <see cref="ApiKey"/>).
    /// </summary>
    public string Endpoint { get; set; } = "http://ip-api.com/json";

    /// <summary>
    /// Optional API key for the ip-api.com pro tier.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Response language for the (unused) country name field. ip-api.com
    /// supports <c>en</c>, <c>de</c>, <c>es</c>, <c>pt-BR</c>, <c>fr</c>,
    /// <c>ja</c>, <c>zh-CN</c>, <c>ru</c>. Default is <c>en</c>. The
    /// SDK only consumes the <c>countryCode</c> field, which is always
    /// ISO-3166-1 alpha-2 regardless of language.
    /// </summary>
    public string Lang { get; set; } = "en";

    /// <summary>
    /// Per-IP cache TTL in seconds. Successful lookups are cached for
    /// this duration to respect the 45 req/min rate limit across
    /// repeated logins from the same client. Defaults to 5 minutes.
    /// Set to <c>0</c> to disable caching entirely.
    /// </summary>
    public int CacheTtlSeconds { get; set; } = 300;

    /// <summary>
    /// HTTP request timeout in milliseconds. The lookup is on the
    /// critical path of every login, so keep this short and fall back
    /// to the configured default country code on timeout. Defaults to
    /// 2 seconds.
    /// </summary>
    public int TimeoutMilliseconds { get; set; } = 2000;
}

/// <summary>
/// Static webstatic controller configuration. Known mi18n version JSON
/// paths are answered directly from <see cref="VersionMap"/>; everything
/// else under <c>/webstatic</c>, <c>/sdk-public</c>, <c>/launcher-public</c>
/// and <c>/admin/mi18n</c> is served from <see cref="ResourceRoot"/> when
/// that directory exists.
/// </summary>
public sealed class WebstaticConfig
{
    /// <summary>
    /// Optional filesystem root for serving arbitrary static SDK files
    /// (<c>webstatic/</c>, <c>sdk-public/</c>, <c>launcher-public/</c>).
    /// When <c>null</c> or non-existent, only the paths in
    /// <see cref="VersionMap"/> are answered; everything else returns 404.
    /// </summary>
    public string? ResourceRoot { get; set; } = "";

    /// <summary>
    /// In-memory map of <c>*-version.json</c> paths to their version
    /// numbers. Pre-populated with the two mi18n paths the Genshin client
    /// hits at startup.
    /// </summary>
    public Dictionary<string, int> VersionMap { get; set; } = new() {
        ["/admin/mi18n/plat_oversea/m2020030410/m2020030410-version.json"] = 107,
        ["/admin/mi18n/plat_os/m09291531181441/m09291531181441-version.json"] = 16
    };
}

/// <summary>
/// Configuration surfaced by the shield and combo-granter <c>getConfig</c>
/// endpoints.
/// </summary>
public sealed class SdkShieldConfig
{
    // /hk4e_global/mdk/shield/api/loadConfig
    public bool EnableGuestLogin { get; set; } = true;
    public bool DisableRegistrations { get; set; } = false;
    public bool UseEmailCaptcha { get; set; } = false;
    public bool DisableMmt { get; set; } = false;
    public bool EnableServerGuest { get; set; } = false;
    public bool EnablePSBindAccount { get; set; } = false;
    public bool EnableFireBase { get; set; } = false;
    public bool EnableFireBaseBlacklistDevicesSwitch { get; set; } = false;
    public int FireBaseBlacklistVersion { get; set; } = 1;
    public bool EnableBBSAuthLogin { get; set; } = false;
    public bool EnableFlashLogin { get; set; } = false;
    public bool EnableAdultLogoAndroid { get; set; } = false;
    public int AdultLogoAndroidHeight { get; set; } = 0;
    public int AdultLogoAndroidWidth { get; set; } = 0;
    public bool EnableCXBindAccount { get; set; } = false;
    public bool EnableHoyoLabAuthLogin { get; set; } = false;
    public bool EnableHoyoPlayAuthLogin { get; set; } = false;
    public bool FetchCurrentInstanceId { get; set; } = true;
    public bool ModifyRealNameOtherVerify { get; set; } = false;
    public bool DisableTryVerify { get; set; } = false;
    public bool EnableRegisterHide { get; set; } = true;
    public bool EnableLoginFlowNotification { get; set; } = true;
    public List<string> ThirdPartyApps { get; set; } = new() {
        ThirdPartyApp.Apple,
        ThirdPartyApp.Google,
        ThirdPartyApp.Facebook,
        ThirdPartyApp.Twitter,
        ThirdPartyApp.GameCenter,
        ThirdPartyApp.TapTap
    };
    public Dictionary<string, string> ThirdPartyIgnored { get; set; } = new();

    /// <summary>
    /// Per-app third-party login token configurations. Typed as
    /// <see cref="ThirdPartyTokenConfig"/> rather than
    /// <c>Dictionary&lt;string, Dictionary&lt;string, object&gt;&gt;</c>;
    /// keys are <see cref="ThirdPartyApp"/> constants.
    /// </summary>
    public Dictionary<string, ThirdPartyTokenConfig> ThirdPartyConfigs { get; set; } = new() {
        [ThirdPartyApp.Facebook] = new ThirdPartyTokenConfig { GameTokenExpiresIn = TokenExpiry.ThirtyDays },
        [ThirdPartyApp.GameCenter] = new ThirdPartyTokenConfig { GameTokenExpiresIn = TokenExpiry.SevenDays },
        [ThirdPartyApp.Twitter] = new ThirdPartyTokenConfig { GameTokenExpiresIn = TokenExpiry.ThirtyDays },
        [ThirdPartyApp.Apple] = new ThirdPartyTokenConfig { GameTokenExpiresIn = TokenExpiry.SevenDays },
        [ThirdPartyApp.Google] = new ThirdPartyTokenConfig { GameTokenExpiresIn = TokenExpiry.ThirtyDays }
    };
    public List<string> BBSAuthLoginIgnored { get; set; } = new();
    public List<string> HoyoLabAuthIgnore { get; set; } = new();

    // /hk4e_global/combo/granter/api/getConfig
    public bool UseQRLogin { get; set; } = true;
    public string ApiLogLevel { get; set; } = SdkDefaults.ApiLogLevel;
    public string AnnouncementUrl { get; set; } =
        "https://sdk.mihoyo.com/hk4e/announcement/index.html?sdk_presentation_style=fullscreen&sdk_screen_transparent=true&auth_appid=announcement&authkey_ver=1&game_biz=hk4e_cn&sign_type=2&version=2.33&game=hk4e#/";
    public int AliasPushType { get; set; } = 2;
    public bool DisableYSDKGuard { get; set; } = false;
    public bool EnableAnnouncementPopUp { get; set; } = true;
    public string AppName { get; set; } = "原神";
    public bool UseAccountCenter { get; set; } = true;
    public string QrCloudDisplayName { get; set; } = "云·原神";
    public Dictionary<string, bool> QrApps { get; set; } = new() { ["bbs"] = true, ["cloud"] = true };
    public Dictionary<string, string> QrAppIcons { get; set; } = new() {
        ["app"] = "https://sdk-webstatic.mihoyo.com/sdk-public/2023/10/11/63b6857bddb8be0887185890596b983f_4890465413038841959.png",
        ["bbs"] = "https://sdk-webstatic.mihoyo.com/sdk-public/2023/10/11/69172b1a1fd17290b3e0649632216372_106775796556262449.png",
        ["cloud"] = "https://sdk-webstatic.mihoyo.com/sdk-public/2022/12/07/ec0f2514f044ac43754440241ab0b838_3962973103776517937.png"
    };
    public bool JPushConfig { get; set; } = false;
    public bool InitializeAppsFlyerConfig { get; set; } = false;
    public bool AllowNotificationConfig { get; set; } = true;
}

/// <summary>
/// Configuration returned by the <c>/combo/box/api/config/**</c>
/// family of endpoints.
/// </summary>
public sealed class SdkComboBoxConfig
{
    // sdk/combo
    public bool EnableTelemetryDataUpload { get; set; } = true;
    public bool EnableTelemetryH5Log { get; set; } = true;
    public bool EnableApmSdk { get; set; } = true;
    public bool EnableEmailBindRemind { get; set; } = true;
    public int EmailBindRemindInterval { get; set; } = 7;
    public bool EnableAttribution { get; set; } = true;
    public bool DisableEmailBindSkip { get; set; } = false;
    public bool EnableNewRegisterPage { get; set; } = true;
    public bool EnableH5Log { get; set; } = true;
    public bool EnableAppsFlyer { get; set; } = true;
    public bool EnableRegisterAutoLogin { get; set; } = true;
    public bool EnableUserCenterV2 { get; set; } = true;
    public bool EnableTwitterV2 { get; set; } = true;
    public bool EnableBindGoogleSdkOrder { get; set; } = true;
    public bool EnableHttpDns { get; set; } = true;
    public int HttpDnsCacheExpireTime { get; set; } = 60;
    public int HttpKeepAliveTime { get; set; } = 60;
    public bool EnableListPriceTierV2 { get; set; } = true;
    public bool EnableNetworkReport { get; set; } = true;
    public List<int> NetworkStatusCodes { get; set; } = new() { HttpStatus.Ok };
    public List<string> NetworkUrlPaths { get; set; } = new() { "combo/postman/device/setAlias" };
    public List<string> NetworkConfigs { get; set; } = new() {
        "report_set_info", "notice_close_notice", "apm_crash_add_custom_key_value",
        "hasScanFunc", "push_clear_local_notification", "push_add_local_notification",
        "launch_del_notification", "info_get_device_id", "getDeviceId",
        "info_get_channel_id", "info_get_sub_channel_id", "login_set_server_id",
        "info_get_cps", "info_get_uapc"
    };

    // PC-specific (client_type 3, 9)
    public bool EnableNewKcp { get; set; } = false;
    public bool EnableKibana { get; set; } = true;
    public List<string> KibanaModules { get; set; } = new() { "download" };
    public bool UseLegacyWebViewRenderMethod { get; set; } = true;
    public bool EnableWebDpi { get; set; } = true;
    public bool EnableAccountListPage { get; set; } = true;
    public bool EnableNewForgotPwdPage { get; set; } = true;
    public bool EnableCrashCapture { get; set; } = false;
    public string PayCoCenteredHost { get; set; } = "bill.payco.com";
    public bool EnableH5Cashier { get; set; } = true;
    public int H5CashierTimeout { get; set; } = 3;

    // Android-specific (client_type 2, 8)
    public bool EnableAliPayRecommend { get; set; } = true;
    public bool WatermarkEnableWebNotice { get; set; } = true;
    public bool EnableOaid { get; set; } = true;
    public bool EnableOaidMultiProcess { get; set; } = true;
    public bool EnableOaidCallHms { get; set; } = true;
    public bool EnablePayPlatformBlockH5Cashier { get; set; } = true;
    public int PayPlatformH5LoadingLimit { get; set; } = 3;
    public bool EnableGoogleV2 { get; set; } = true;
    public bool EnableGoogleCancelCallback { get; set; } = true;

    // Console-specific (client_type 10, 13)
    public bool EnableConsoleTelemetryUpload { get; set; } = true;

    // sw/precache
    public bool EnableServiceWorker { get; set; } = true;
    public string ServiceWorkerUrl { get; set; } = "https://webstatic-sea.hoyoverse.com/sw.html";
}

/// <summary>
/// Configuration returned by
/// <c>/hk4e_global/account/ma-passport/api/getConfig</c>.
/// </summary>
public sealed class SdkMaPassportConfig
{
    public string Language { get; set; } = SdkDefaults.MaPassportLanguage;
    public List<string> AreaWhitelist { get; set; } = new() { "KR" };
    public List<string> RealnameWhitelist { get; set; } = new();
    public string GuardianAgeLimit { get; set; } = SdkDefaults.GuardianAgeLimit;
    public bool DisableMmt { get; set; } = false;
    public bool ShowBirthday { get; set; } = false;

    /// <summary>
    /// Tunables for the <c>appLoginByPassword</c> /
    /// <c>appLoginByAuthTicket</c> / <c>reactivateAccount</c> login flows.
    /// </summary>
    public SdkMaPassportLoginConfig Login { get; set; } = new();
}

/// <summary>
/// Login-flow tunables for the ma-passport endpoints.
/// </summary>
public sealed class SdkMaPassportLoginConfig
{
    /// <summary>
    /// Minimum accepted length of an (RSA-decrypted) password for the
    /// <c>appLoginByPassword</c> endpoint. Upstream uses <c>8</c>.
    /// </summary>
    public int MinPasswordLength { get; set; } = 8;

    /// <summary>
    /// Maximum accepted length of an (RSA-decrypted) password for the
    /// <c>appLoginByPassword</c> endpoint.
    /// </summary>
    public int MaxPasswordLength { get; set; } = 50;

    /// <summary>
    /// When <c>true</c>, the <c>appLoginByPassword</c> endpoint skips the
    /// "device id must be in the account's known-device list" check.
    /// Intended for local development where the device id changes between
    /// test runs. Never enable in production.
    /// </summary>
    public bool SkipDeviceIdCheck { get; set; }

    /// <summary>
    /// When <c>true</c>, both <c>account</c> and <c>password</c> are
    /// treated as plain text rather than RSA-encrypted blobs. Intended
    /// for local development where the client isn't built with the
    /// matching RSA public key. Never enable in production.
    /// </summary>
    public bool SkipRsaDecryption { get; set; }

    /// <summary>
    /// Length of the random tokens issued by the ma-passport login flows
    /// (session key, stoken, etc.).
    /// </summary>
    public int TokenLength { get; set; } = 30;

    /// <summary>
    /// Token type reported in the response of <c>appLoginByPassword</c>
    /// (and <c>reactivateAccount</c>). Stored as <c>int</c> here so the
    /// configuration JSON stays numeric; cast to
    /// <see cref="MaPassportTokenType"/> at the call site.
    /// </summary>
    public int AppLoginTokenType { get; set; } = (int)MaPassportTokenType.Stoken;

    /// <summary>
    /// Token type reported in the response of <c>appLoginByAuthTicket</c>
    /// and <c>verifySToken</c>. Stored as <c>int</c> here so the
    /// configuration JSON stays numeric; cast to
    /// <see cref="MaPassportTokenType"/> at the call site.
    /// </summary>
    public int AuthTicketTokenType { get; set; } = (int)MaPassportTokenType.Stoken;

    /// <summary>
    /// When <c>true</c>, the <c>getSwitchStatus</c> endpoint enables the
    /// Android-only <c>ui_v2</c> switch for <c>platform=2</c>.
    /// </summary>
    public bool EnableAndroidUiV2 { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, the <c>getSwitchStatus</c> endpoint reports
    /// <c>apple_login</c> / <c>google_login</c> / <c>twitter_login</c> /
    /// <c>facebook_login</c> as enabled.
    /// </summary>
    public bool EnableThirdPartyLogins { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, the <c>getSwitchStatus</c> endpoint reports
    /// <c>account_register_tab</c> and <c>pwd_login_tab</c> as enabled.
    /// </summary>
    public bool EnableLoginRegisterTabs { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, the <c>getSwitchStatus</c> endpoint reports
    /// <c>vn_real_name</c> and <c>vn_real_name_v2</c> as enabled.
    /// Disabled by default to match the overseas deployment.
    /// </summary>
    public bool EnableVietnamRealName { get; set; } = false;
}

/// <summary>
/// Configuration returned by
/// <c>/data_abtest_api/config/experiment/list</c>. Starlight does not
/// persist experiments; the response is built from this static list
/// filtered by the requested scene id.
/// </summary>
public sealed class SdkAbTestConfig
{
    /// <summary>
    /// When <c>true</c>, the experiment list endpoint always returns an
    /// empty data array even if <see cref="Experiments"/> is non-empty.
    /// Convenient for stubbing the response during local testing.
    /// </summary>
    public bool ReturnEmptyList { get; set; } = true;

    public List<SdkExperiment> Experiments { get; set; } = new();
}

public sealed class SdkExperiment
{
    public int Code { get; set; }
    public int Type { get; set; }
    public string ConfigId { get; set; } = string.Empty;
    public string PeriodId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public Dictionary<string, string> Configs { get; set; } = new();
    public bool SceneWhiteList { get; set; }
    public bool ExperimentWhiteList { get; set; }
}

/// <summary>
/// Configuration returned by <c>/device-fp/api/getExtList</c>. Starlight
/// does not persist device extensions per-platform; the response is built
/// from this static table.
/// </summary>
public sealed class SdkDeviceFpConfig
{
    /// <summary>
    /// Per-platform device extension entries. Keyed by the integer value
    /// of <see cref="Common.PlatformId"/> (1..13). Missing platforms return an
    /// empty ext_list with success code.
    /// </summary>
    public Dictionary<int, SdkDeviceExt> Extensions { get; set; } = new();
}

public sealed class SdkDeviceExt
{
    public List<string> Ext { get; set; } = new();
    public List<string> Pkgs { get; set; } = new();
    public string PkgStr { get; set; } = string.Empty;
}
