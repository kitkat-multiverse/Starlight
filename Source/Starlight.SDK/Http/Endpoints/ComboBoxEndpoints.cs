using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Starlight.Common;
using Starlight.SDK.Common;
using Starlight.SDK.Http;
using Starlight.SDK.Http.Models;

namespace Starlight.SDK.Http.Endpoints;

/// <summary>
/// Implements the <c>/combo/box/api/config/sdk/combo</c> and
/// <c>/combo/box/api/config/sw/precache</c> endpoints.
/// </summary>
public static class ComboBoxEndpoints
{
    private static readonly string[] PathPrefixes =
        ["/hk4e_global/combo/box/api/config", "/hk4e_cn/combo/box/api/config", "/combo/box/api/config"];

    public static void MapComboBoxEndpoints(this IEndpointRouteBuilder routes)
    {
        foreach (var prefix in PathPrefixes)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                continue;

            routes.MapGet($"{prefix}/sdk/combo", HandleSdkCombo);
            routes.MapGet($"{prefix}/sw/precache", HandleSwPrecache);
        }
    }

    private static IResult HandleSdkCombo(
        [FromQuery] string? biz_key,
        [FromQuery] int? client_type,
        [FromServices] SdkConfig sdkConfig
    )
    {
        if (string.IsNullOrEmpty(biz_key) || client_type is null
                                          || !SdkUtils.IsValidGameBiz(biz_key))
        {
            return Results.Ok(ApiResponse.From(Retcode.ComboInvalidKey));
        }

        // Reject anything that isn't a known platform id.
        if (!Enum.IsDefined(typeof(PlatformId), client_type.Value))
            return Results.Ok(ApiResponse.From(Retcode.ComboPlatformNoConfig));

        var platform = (PlatformId)client_type.Value;

        if (platform.IsUnsupportedByComboBox())
            return Results.Ok(ApiResponse.From(Retcode.ComboPlatformNoConfig));

        var shield = sdkConfig.Shield;
        var box = sdkConfig.ComboBox;
        var vals = new Dictionary<string, string>();

        switch (platform)
        {
            case PlatformId.BrowserA or PlatformId.BrowserB:
                vals[ComboBoxConfigKey.ModifyRealNameOtherVerify] = Bool(shield.ModifyRealNameOtherVerify);
                vals[ComboBoxConfigKey.LoginFlowNotification] = JsonBool("enable", shield.EnableLoginFlowNotification);
                break;
            case PlatformId.PlayStation5:
                vals[ComboBoxConfigKey.LoginFlowNotification] = JsonBool("enable", shield.EnableLoginFlowNotification);
                break;
            case PlatformId.CloudIos or PlatformId.CloudMacOs:
                vals[ComboBoxConfigKey.EnableTelemetryDataUpload] = Bool(box.EnableConsoleTelemetryUpload);
                vals[ComboBoxConfigKey.EnableTelemetryH5Log] = Bool(box.EnableTelemetryH5Log);
                vals[ComboBoxConfigKey.NetworkReportConfig] = "{\n\t\"enable\": 1,\n\t\"status_codes\": [200]\n}";
                break;
            case PlatformId.Android or PlatformId.CloudAndroid:
                vals[ComboBoxConfigKey.AlipayRecommend] = Bool(box.EnableAliPayRecommend);
                vals[ComboBoxConfigKey.WatermarkEnableWebNotice] = Bool(box.WatermarkEnableWebNotice);
                vals[ComboBoxConfigKey.EnableOaid] = Bool(box.EnableOaid);
                vals[ComboBoxConfigKey.OaidMultiProcess] = Bool(box.EnableOaidMultiProcess);
                vals[ComboBoxConfigKey.OaidCallHms] = Bool(box.EnableOaidCallHms);
                vals[ComboBoxConfigKey.PayPlatformBlockH5Cashier] = Bool(box.EnablePayPlatformBlockH5Cashier);
                vals[ComboBoxConfigKey.PayPlatformH5LoadingLimit] = box.PayPlatformH5LoadingLimit.ToString();
                vals[ComboBoxConfigKey.IsGooglePayV2] = Bool(box.EnableGoogleV2);
                vals[ComboBoxConfigKey.ReportBlackList] = "{\"key\":[\"download_update_progress\"]}";
                vals[ComboBoxConfigKey.BiliPayCancelStrings] = "[\"用户取消交易\"]\n";
                vals[ComboBoxConfigKey.EnableGoogleCancelCallback] = Bool(box.EnableGoogleCancelCallback);
                goto case PlatformId.Pc;
            case PlatformId.Pc:
            case PlatformId.CloudPc:
                vals[ComboBoxConfigKey.KcpEnable] = Bool(box.EnableNewKcp);

                vals[ComboBoxConfigKey.KibanaPcConfig] = JsonSerializer.Serialize(new {
                    enable = box.EnableKibana ? 1 : 0,
                    level = Capitalize(shield.ApiLogLevel),
                    modules = box.KibanaModules
                });

                vals[ComboBoxConfigKey.WebViewRenderMethodConfig] = JsonSerializer.Serialize(new {
                    useLegacy = box.UseLegacyWebViewRenderMethod
                });
                vals[ComboBoxConfigKey.EnableWebDpi] = Bool(box.EnableWebDpi);
                vals[ComboBoxConfigKey.AccountListPageEnable] = Bool(box.EnableAccountListPage);
                vals[ComboBoxConfigKey.NewForgotPwdPageEnable] = Bool(box.EnableNewForgotPwdPage);

                vals[ComboBoxConfigKey.WebViewApmConfig] = JsonSerializer.Serialize(new {
                    crash_capture_enable = box.EnableCrashCapture
                });
                vals[ComboBoxConfigKey.PayPaycoCenteredHost] = box.PayCoCenteredHost;

                vals[ComboBoxConfigKey.PaymentCnConfig] = JsonSerializer.Serialize(new {
                    h5_cashier_enable = box.EnableH5Cashier ? 1 : 0,
                    h5_cashier_timeout = box.H5CashierTimeout
                });
                goto default;
            default:
                vals[ComboBoxConfigKey.EnableTelemetryDataUpload] = Bool(box.EnableTelemetryDataUpload);

                vals[ComboBoxConfigKey.TelemetryConfig] = JsonSerializer.Serialize(new {
                    dataupload_enable = box.EnableTelemetryDataUpload
                });
                vals[ComboBoxConfigKey.EnableApmSdk] = Bool(box.EnableApmSdk);
                vals[ComboBoxConfigKey.EnableTelemetryH5Log] = Bool(box.EnableTelemetryH5Log);
                vals[ComboBoxConfigKey.EmailBindRemind] = Bool(box.EnableEmailBindRemind);
                vals[ComboBoxConfigKey.EmailBindRemindInterval] = box.EmailBindRemindInterval.ToString();
                vals[ComboBoxConfigKey.EnableAttribution] = Bool(box.EnableAttribution);
                vals[ComboBoxConfigKey.DisableEmailBindSkip] = Bool(box.DisableEmailBindSkip);
                vals[ComboBoxConfigKey.NewRegisterPageEnable] = Bool(box.EnableNewRegisterPage);

                vals[ComboBoxConfigKey.H5LogConfig] = JsonSerializer.Serialize(new {
                    enable = box.EnableH5Log,
                    level = Capitalize(shield.ApiLogLevel)
                });

                vals[ComboBoxConfigKey.AppsFlyerConfig] = JsonSerializer.Serialize(new {
                    enable = box.EnableAppsFlyer
                });
                vals[ComboBoxConfigKey.EnableRegisterAutoLogin] = Bool(box.EnableRegisterAutoLogin);
                vals[ComboBoxConfigKey.EnableUserCenterV2] = Bool(box.EnableUserCenterV2);
                vals[ComboBoxConfigKey.EnableTwitterV2] = Bool(box.EnableTwitterV2);
                vals[ComboBoxConfigKey.ModifyRealNameOtherVerify] = Bool(shield.ModifyRealNameOtherVerify);
                vals[ComboBoxConfigKey.DisableTryVerify] = Bool(shield.DisableTryVerify);
                vals[ComboBoxConfigKey.LoginFlowNotification] = JsonBool("enable", shield.EnableLoginFlowNotification);

                vals[ComboBoxConfigKey.NetworkReportConfig] = JsonSerializer.Serialize(new {
                    enable = box.EnableNetworkReport ? 1 : 0,
                    status_codes = box.NetworkStatusCodes,
                    url_paths = box.NetworkUrlPaths
                });
                vals[ComboBoxConfigKey.EnableBindGoogleSdkOrder] = Bool(box.EnableBindGoogleSdkOrder);
                vals[ComboBoxConfigKey.EmailRegisterHide] = Bool(shield.EnableRegisterHide);
                vals[ComboBoxConfigKey.HttpDnsEnable] = Bool(box.EnableHttpDns);
                vals[ComboBoxConfigKey.HttpDnsCacheExpireTime] = box.HttpDnsCacheExpireTime.ToString();
                vals[ComboBoxConfigKey.HttpKeepAliveTime] = box.HttpKeepAliveTime.ToString();
                vals[ComboBoxConfigKey.ListPriceTierV2Enable] = Bool(box.EnableListPriceTierV2);

                vals[ComboBoxConfigKey.H5LogFilterConfig] = JsonSerializer.Serialize(new {
                    function = new { event_name = box.NetworkConfigs }
                });
                break;
        }

        return Results.Ok(ApiResponse.Ok(new ComboBoxConfigData { Vals = vals }));
    }

    private static IResult HandleSwPrecache(
        [FromQuery] string? biz,
        [FromQuery] int? client,
        [FromServices] SdkConfig sdkConfig
    )
    {
        if (string.IsNullOrEmpty(biz) || client is null
                                      || !SdkUtils.IsValidGameBiz(biz))
        {
            return Results.Ok(ApiResponse.From(Retcode.ComboInvalidKey));
        }

        // Precache only exists for the three PC-style platforms (PC,
        // Cloud PC, Cloud Android). Any other id is rejected.
        if (!Enum.IsDefined(typeof(PlatformId), client.Value)
            || client.Value is not ((int)PlatformId.Pc
                or (int)PlatformId.CloudPc
                or (int)PlatformId.CloudAndroid))
        {
            return Results.Ok(ApiResponse.From(Retcode.ComboPlatformNoConfig));
        }

        return Results.Ok(ApiResponse.Ok(new ComboBoxPrecacheData {
            Data = new ComboBoxPrecacheInner {
                Enable = sdkConfig.ComboBox.EnableServiceWorker.ToString().ToLowerInvariant(),
                Url = sdkConfig.ComboBox.ServiceWorkerUrl
            }
        }));
    }

    private static string Bool(bool value) => value.ToString().ToLowerInvariant();

    private static string JsonBool(string key, bool value)
        => JsonSerializer.Serialize(new Dictionary<string, object> { [key] = value });

    private static string Capitalize(string value)
        => string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
}
