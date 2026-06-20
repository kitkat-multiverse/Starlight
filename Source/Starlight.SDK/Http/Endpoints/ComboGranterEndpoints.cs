using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Starlight.SDK.Common;
using Starlight.Crypto;
using Starlight.SDK.Http.Models;
using Starlight.SDK.Services;

namespace Starlight.SDK.Http.Endpoints;

public static class ComboGranterEndpoints
{
    private static readonly string[] PathPrefixes = [
        "/hk4e_global/combo/granter/api",
        "/hk4e_global/combo/granter/login",
        "/hk4e_cn/combo/granter/api",
        "/hk4e_cn/combo/granter/login",
        "/combo/granter/api"
    ];

    public static void MapComboGranterEndpoints(this IEndpointRouteBuilder routes)
    {
        foreach (var prefix in PathPrefixes)
        {
            routes.MapPost($"{prefix}/v2/login", HandleLoginV2Async);
            routes.MapPost($"{prefix}/login", HandleLoginV2Async);
            routes.MapGet($"{prefix}/getConfig", HandleGetConfig);
            routes.MapPost($"{prefix}/getConfig", HandleGetConfig);
            routes.MapPost($"{prefix}/compareProtocolVersion", HandleCompareProtocolVersion);
            routes.MapPost($"{prefix}/getProtocol", HandleCompareProtocolVersion);
            routes.MapGet($"{prefix}/compareProtocolVersion", HandleCompareProtocolVersionGet);
            routes.MapGet($"{prefix}/getProtocol", HandleCompareProtocolVersionGet);
        }
    }

    private static async Task<IResult> HandleLoginV2Async(
        HttpContext httpContext,
        [FromBody] ComboGranterLoginRequest? body,
        [FromHeader(Name = "x-rpc-device_id")] string? deviceId,
        [FromServices] IAuthService auth,
        [FromServices] IGeoIpLookup geoIp,
        [FromServices] SdkConfig sdkConfig,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct
    )
    {
        // A static type can't be a generic type argument (CS0718), so
        // ILogger<T> isn't an option here. The factory caches by category,
        // so this lookup is cheap.
        var logger = loggerFactory.CreateLogger(typeof(ComboGranterEndpoints).FullName!);

        if (body is null
            || body.AppId is null
            || body.ChannelId is null
            || string.IsNullOrEmpty(body.Data)
            || string.IsNullOrEmpty(body.Device)
            || string.IsNullOrEmpty(deviceId)
            || !SdkUtils.IsValidDeviceId(deviceId))
        {
            return Results.Ok(ApiResponse.From(Retcode.ParameterError));
        }

        if (!SdkUtils.IsValidAppId(body.AppId.GetValueOrDefault()))
        {
            logger.LogDebug("Rejected combo granter login: app_id {AppId} is not an ApplicationId", body.AppId);
            return Results.Ok(ApiResponse.From(Retcode.ParameterError));
        }

        if (!string.Equals(body.Device, deviceId, StringComparison.Ordinal))
            return Results.Ok(ApiResponse.From(Retcode.LoginNetworkAtRisk));

        // HMAC signature verification, skipped only when explicitly disabled
        // (debug builds, integration tests).
        if (!sdkConfig.SkipSignatureCheck)
        {
            if (string.IsNullOrEmpty(body.Sign))
                return Results.Ok(ApiResponse.From(Retcode.ParameterError));

            if (string.IsNullOrEmpty(sdkConfig.HmacKey))
            {
                // Already warned at startup, Debug-only here to avoid spamming.
                logger.LogDebug("ComboGranter HMAC key is not configured but SkipSignatureCheck=false");
                return Results.Ok(ApiResponse.From(Retcode.SystemError));
            }

            var canonical = CreateMessage(body);

            if (!HmacCrypto.Verify(canonical, sdkConfig.HmacKey, body.Sign!))
                return Results.Ok(ApiResponse.From(Retcode.MissingConfiguration));
        }

        ComboLoginV2Data? inner;

        try
        {
            inner = JsonSerializer.Deserialize<ComboLoginV2Data>(body.Data!);
        }
        catch (JsonException)
        {
            return Results.Ok(ApiResponse.From(Retcode.InvalidJsonBody));
        }

        if (inner is null || string.IsNullOrEmpty(inner.Token))
            return Results.Ok(ApiResponse.From(Retcode.ParameterError));

        var result = await auth.ExchangeComboTokenAsync(inner.Token!, deviceId!, ct);

        if (!result.IsSuccess || result.Account is null)
            return Results.Ok(ApiResponse.From(result.Code));

        var acc = result.Account;

        var remoteIp = SdkHttpHelpers.GetClientIp(httpContext);
        var countryCode = await geoIp.GetCountryCodeAsync(remoteIp, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(countryCode))
            countryCode = acc.Country;

        if (string.IsNullOrWhiteSpace(countryCode))
            countryCode = sdkConfig.DefaultCountryCode;

        var isGuest = acc.AccountType == AccountType.Guest;

        var innerJson = JsonSerializer.Serialize(new ComboInnerData {
            Guest = isGuest,
            CountryCode = countryCode,
            IsNewRegister = false
        });

        var payload = new ComboGranterLoginResponse {
            ComboId = sdkConfig.DefaultComboId,
            OpenId = acc.Id.ToString(),
            ComboToken = acc.ComboToken,
            Data = innerJson,
            Heartbeat = false,
            AccountType = acc.AccountType,
            FatigueRemind = null
        };

        return Results.Ok(ApiResponse.Ok(payload));
    }

    private static string CreateMessage(ComboGranterLoginRequest body)
    {
        var pairs = new SortedDictionary<string, string> {
            ["app_id"] = body.AppId!.Value.ToString(),
            ["channel_id"] = body.ChannelId!.Value.ToString(),
            ["data"] = body.Data!,
            ["device"] = body.Device!
        };

        return string.Join("&", pairs.Select(p => $"{p.Key}={p.Value}"));
    }

    /// <summary>
    /// Handles <c>GET/POST /hk4e_global/combo/granter/api/getConfig</c>.
    /// Returns the SDK-level configuration: announce URL, log level, QR
    /// login settings, etc. Client-type-specific switch configs (jpush,
    /// appsflyer, notifications) are populated per platform.
    /// </summary>
    private static IResult HandleGetConfig(
        [FromQuery] int? appId,
        [FromQuery] int? channelId,
        [FromQuery] int? clientType,
        [FromServices] SdkConfig sdkConfig
    )
    {
        if (appId is null || channelId is null || clientType is null
            || !Enum.IsDefined(typeof(PlatformId), clientType.Value))
        {
            return Results.Ok(ApiResponse.From(Retcode.SystemError));
        }

        if (!SdkUtils.IsValidAppId(appId.GetValueOrDefault()))
            return Results.Ok(ApiResponse.From(Retcode.SystemError));

        var platform = (PlatformId)clientType.Value;
        var s = sdkConfig.Shield;

        var data = new ComboGranterConfigData {
            // channel_id == Official (1) -> protocol not modified;
            // any other channel -> protocol modified (i.e. needs ack).
            Protocol = channelId != (int)ChannelId.Official,
            QrEnabled = s.UseQRLogin,
            LogLevel = s.ApiLogLevel,
            AnnounceUrl = s.AnnouncementUrl,
            PushAliasType = s.AliasPushType,
            DisableYsdkGuard = s.DisableYSDKGuard,
            EnableAnnouncePicPopup = s.EnableAnnouncementPopUp,
            AppName = s.AppName,
            QrCloudDisplayName = s.QrCloudDisplayName,
            EnableUserCenter = s.UseAccountCenter,
            FunctionalSwitchConfigs = new Dictionary<string, bool>()
        };

        // QR settings only apply to PC-style platforms.
        if (platform.IsPcLike())
        {
            data.QrEnabledApps = s.QrApps;
            data.QrAppIcons = s.QrAppIcons;
        }

        switch (platform)
        {
            case PlatformId.Ios or PlatformId.Android or PlatformId.CloudAndroid:
                data.FunctionalSwitchConfigs[FunctionalSwitchKey.InitializeAppsFlyer] = s.InitializeAppsFlyerConfig;

                if (platform is PlatformId.Ios or PlatformId.Android)
                {
                    data.FunctionalSwitchConfigs[FunctionalSwitchKey.JPush] = s.JPushConfig;

                    if (platform is PlatformId.Android)
                        data.FunctionalSwitchConfigs[FunctionalSwitchKey.AllowNotification] = s.AllowNotificationConfig;
                }

                break;
        }

        return Results.Ok(ApiResponse.Ok(data));
    }

    private static IResult HandleCompareProtocolVersion(
        [FromBody] CompareProtocolVersionRequest? body
    )
    {
        if (body is null || string.IsNullOrEmpty(body.Language)
                         || !SdkUtils.IsValidLanguage(body.Language))
        {
            return Results.Ok(ApiResponse.From(Retcode.ProtocolFailed));
        }

        if (!SdkUtils.IsValidAppId(body.AppId))
            return Results.Ok(ApiResponse.From(Retcode.ProtocolFailed));

        var isModified = body.ChannelId != (int)ChannelId.Official;

        var data = new CompareProtocolVersionData {
            Modified = isModified,
            Protocol = isModified ?
                new ProtocolInfo {
                    Id = 0,
                    AppId = body.AppId,
                    Language = body.Language!,
                    Major = body.Major,
                    Minimum = body.Minimum
                } :
                null
        };

        return Results.Ok(ApiResponse.Ok(data));
    }

    private static IResult HandleCompareProtocolVersionGet(
        [FromQuery] int? app_id,
        [FromQuery] string? language,
        [FromQuery] int? major,
        [FromQuery] int? minimum,
        [FromQuery] int? channel_id
    )
    {
        if (app_id is null || string.IsNullOrEmpty(language)
                           || !SdkUtils.IsValidLanguage(language) || major is null || minimum is null)
        {
            return Results.Ok(ApiResponse.From(Retcode.ProtocolFailed));
        }

        if (!SdkUtils.IsValidAppId(app_id.Value))
            return Results.Ok(ApiResponse.From(Retcode.ProtocolFailed));

        var isModified = channel_id is null || channel_id.Value != (int)ChannelId.Official;

        var data = new CompareProtocolVersionData {
            Modified = isModified,
            Protocol = isModified ?
                new ProtocolInfo {
                    Id = 5,
                    AppId = app_id.Value,
                    Language = language!,
                    Major = major.Value,
                    Minimum = minimum.Value
                } :
                null
        };

        return Results.Ok(ApiResponse.Ok(data));
    }
}
