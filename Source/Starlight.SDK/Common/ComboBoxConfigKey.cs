namespace Starlight.SDK.Common;

/// <summary>
/// Wire-format string keys used in the <c>vals</c> dictionary returned by
/// <c>/combo/box/api/config/sdk/combo</c>. See
/// <see cref="Starlight.SDK.Http.Models.ComboBoxConfigData"/>.
/// </summary>
/// <remarks>
/// Previously each key was a string literal next to its setter call in
/// <c>ComboBoxEndpoints.HandleSdkCombo</c>. Centralizing them here makes
/// typos surface at compile time and gives a single place to document
/// what each switch actually controls.
/// </remarks>
public static class ComboBoxConfigKey
{
    // Browser-only (client_type 4, 5)
    public const string ModifyRealNameOtherVerify = "modify_real_name_other_verify";
    public const string LoginFlowNotification = "login_flow_notification";

    // Console-only (client_type 10, 13)
    public const string EnableTelemetryDataUpload = "enable_telemetry_data_upload";
    public const string EnableTelemetryH5Log = "enable_telemetry_h5log";
    public const string NetworkReportConfig = "network_report_config";

    // Android / Cloud Android (client_type 2, 8), also fall through to PC defaults
    public const string AlipayRecommend = "alipay_recommend";
    public const string WatermarkEnableWebNotice = "watermark_enable_web_notice";
    public const string EnableOaid = "enable_oaid";
    public const string OaidMultiProcess = "oaid_multi_process";
    public const string OaidCallHms = "oaid_call_hms";
    public const string PayPlatformBlockH5Cashier = "pay_platform_block_h5_cashier";
    public const string PayPlatformH5LoadingLimit = "pay_platform_h5_loading_limit";
    public const string IsGooglePayV2 = "isGooglePayV2";
    public const string ReportBlackList = "report_black_list";
    public const string BiliPayCancelStrings = "bili_pay_cancel_strings";
    public const string EnableGoogleCancelCallback = "enable_google_cancel_callback";
    public const string KcpEnable = "kcp_enable";
    public const string KibanaPcConfig = "kibana_pc_config";
    public const string WebViewRenderMethodConfig = "webview_rendermethod_config";
    public const string EnableWebDpi = "enable_web_dpi";
    public const string AccountListPageEnable = "account_list_page_enable";
    public const string NewForgotPwdPageEnable = "new_forgotpwd_page_enable";
    public const string WebViewApmConfig = "webview_apm_config";
    public const string PayPaycoCenteredHost = "pay_payco_centered_host";
    public const string PaymentCnConfig = "payment_cn_config";

    // Default block (every supported platform falls through here)
    public const string TelemetryConfig = "telemetry_config";
    public const string EnableApmSdk = "enable_apm_sdk";
    public const string EmailBindRemind = "email_bind_remind";
    public const string EmailBindRemindInterval = "email_bind_remind_interval";
    public const string EnableAttribution = "enable_attribution";
    public const string DisableEmailBindSkip = "disable_email_bind_skip";
    public const string NewRegisterPageEnable = "new_register_page_enable";
    public const string H5LogConfig = "h5log_config";
    public const string AppsFlyerConfig = "appsflyer_config";
    public const string EnableRegisterAutoLogin = "enable_register_autologin";
    public const string EnableUserCenterV2 = "enable_user_center_v2";
    public const string EnableTwitterV2 = "enable_twitter_v2";
    public const string DisableTryVerify = "disable_try_verify";
    public const string EnableBindGoogleSdkOrder = "enable_bind_google_sdk_order";
    public const string EmailRegisterHide = "email_register_hide";
    public const string HttpDnsEnable = "httpdns_enable";
    public const string HttpDnsCacheExpireTime = "httpdns_cache_expire_time";
    public const string HttpKeepAliveTime = "http_keep_alive_time";
    public const string ListPriceTierV2Enable = "list_price_tierv2_enable";
    public const string H5LogFilterConfig = "h5log_filter_config";
}
