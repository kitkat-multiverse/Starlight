using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Starlight.SDK.Common;
using Starlight.SDK.Database;
using Starlight.SDK.Http.Models;
using Starlight.SDK.Services;

namespace Starlight.SDK.Http.Endpoints;

public static class ShieldEndpoints
{
    private static readonly string[] PathPrefixes =
        ["/hk4e_global/mdk/shield/api", "/hk4e_cn/mdk/shield/api", "/mdk/shield/api"];

    public static void MapShieldEndpoints(this IEndpointRouteBuilder routes)
    {
        foreach (var prefix in PathPrefixes)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                continue;

            routes.MapPost($"{prefix}/login", HandleLoginAsync);
            routes.MapGet($"{prefix}/loadConfig", HandleLoadConfig);
            routes.MapPost($"{prefix}/loadConfig", HandleLoadConfig);
        }
    }

    private static async Task<IResult> HandleLoginAsync(
        HttpContext httpContext,
        [FromBody] ShieldLoginRequest? body,
        [FromHeader(Name = "x-rpc-device_id")] string? deviceId,
        [FromHeader(Name = "x-rpc-language")] string? language,
        [FromServices] IAuthService auth,
        [FromServices] IGeoIpLookup geoIp,
        [FromServices] IAccountRepository accounts,
        [FromServices] SdkConfig sdkConfig,
        CancellationToken ct
    )
    {
        // missing core fields are treated as network-at-risk,
        // rather than a plain parameter error so the client
        // surfaces the right UI hint.
        if (body is null
            || string.IsNullOrWhiteSpace(body.Account)
            || string.IsNullOrWhiteSpace(body.Password)
            || body.IsCrypto is null
            || string.IsNullOrWhiteSpace(deviceId)
            || !SdkUtils.IsValidDeviceId(deviceId))
        {
            return Results.Ok(ApiResponse.From(Retcode.LoginNetworkAtRisk));
        }

        if (!SdkUtils.IsValidGameBiz(body.GameKey)
            || !SdkUtils.IsValidLanguage(language))
        {
            return Results.Ok(ApiResponse.From(Retcode.LoginNetworkAtRisk));
        }

        var result = await auth.LoginAsync(
            body.Account!,
            body.Password!,
            body.IsCrypto.GetValueOrDefault(),
            deviceId!,
            ct);

        if (!result.IsSuccess || result.Account is null)
            return Results.Ok(ApiResponse.From(result.Code));

        var acc = result.Account;
        var remoteIp = SdkHttpHelpers.GetClientIp(httpContext);
        var country = await geoIp.GetCountryCodeAsync(remoteIp, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(country))
            country = acc.Country;

        if (string.IsNullOrWhiteSpace(country))
            country = sdkConfig.DefaultCountryCode;

        if (!string.Equals(acc.Country, country, StringComparison.Ordinal))
        {
            acc.Country = country;
            await accounts.UpdateSessionAsync(acc, ct);
        }

        // config default can also be blank, so fall back to the "None"
        // constant explicitly instead of letting a null/empty value sneak
        // into the response.
        var realNameOperation =
            !string.IsNullOrWhiteSpace(acc.RealNameOperation) ? acc.RealNameOperation :
            !string.IsNullOrWhiteSpace(sdkConfig.DefaultRealNameOperation) ? sdkConfig.DefaultRealNameOperation :
            RealNameOperations.None;

        var payload = new ShieldLoginResponse {
            Account = new ShieldAccountInfo {
                Id = acc.Id,
                Name = acc.Username,
                Email = acc.Email,
                Token = acc.SessionToken,
                Country = country
            },
            RealPersonRequired = acc.RequireRealPerson,
            SafeMobileRequired = acc.RequireSafeMobile,
            ReactivateRequired = acc.RequireActivation,
            DeviceGrantRequired = acc.RequireDeviceGrant,
            RealNameOperation = realNameOperation
        };

        return Results.Ok(ApiResponse.Ok(payload));
    }

    /// <summary>
    /// Handles <c>GET/POST /hk4e_global/mdk/shield/api/loadConfig</c>.
    /// Returns the client-side SDK configuration for the requested
    /// platform and game biz. values come from <see cref="SdkShieldConfig"/>.
    /// </summary>
    private static IResult HandleLoadConfig(
        [FromQuery] int? client,
        [FromQuery] string? game_key,
        [FromServices] SdkConfig sdkConfig
    )
    {
        if (string.IsNullOrEmpty(game_key))
            return Results.Ok(ApiResponse.From(Retcode.ParameterError));

        if (client is null
            || !Enum.IsDefined(typeof(PlatformId), client.Value)
            || !SdkUtils.IsValidGameBiz(game_key))
        {
            return Results.Ok(ApiResponse.From(Retcode.MissingConfiguration));
        }

        var platform = (PlatformId)client.Value;
        var s = sdkConfig.Shield;
        var isPhone = platform.IsPhone();

        var data = new ShieldLoadConfigData {
            Id = PlatformConfigMap.GetConfigId(platform),
            GameKey = game_key!,
            AppId = (int)Common.ApplicationId.Release,
            Client = PlatformConfigMap.GetPlatformName(platform),
            Identity = SdkDefaults.ShieldIdentity,
            Guest = s.EnableGuestLogin,
            IgnoreVersions = SdkDefaults.ShieldIgnoreVersions,
            Scene = platform == PlatformId.Pc ? SdkDefaults.ShieldSceneAccount : SdkDefaults.ShieldSceneNormal,
            Name = s.AppName,
            DisableRegist = s.DisableRegistrations,
            EnableEmailCaptcha = s.UseEmailCaptcha,
            Thirdparty = s.ThirdPartyApps,
            DisableMmt = s.DisableMmt,
            ServerGuest = s.EnableServerGuest,
            ThirdpartyIgnore = s.ThirdPartyIgnored,
            EnablePsBindAccount = s.EnablePSBindAccount,
            ThirdpartyLoginConfigs = s.ThirdPartyConfigs,
            InitializeFirebase = isPhone && s.EnableFireBase,
            BbsAuthLogin = s.EnableBBSAuthLogin,
            BbsAuthLoginIgnore = s.BBSAuthLoginIgnored,
            FetchInstanceId = s.FetchCurrentInstanceId,
            EnableFlashLogin = s.EnableFlashLogin,
            EnableLogo18 = s.EnableAdultLogoAndroid,
            LogoHeight = s.AdultLogoAndroidHeight.ToString(),
            LogoWidth = s.AdultLogoAndroidWidth.ToString(),
            EnableCxBindAccount = s.EnableCXBindAccount,
            FirebaseBlacklistDevicesSwitch = isPhone && s.EnableFireBaseBlacklistDevicesSwitch,
            FirebaseBlacklistDevicesVersion = isPhone ? s.FireBaseBlacklistVersion : 0,
            HoyolabAuthLogin = s.EnableHoyoLabAuthLogin,
            HoyolabAuthLoginIgnore = s.HoyoLabAuthIgnore,
            HoyoplayAuthLogin = s.EnableHoyoPlayAuthLogin
        };

        return Results.Ok(ApiResponse.Ok(data));
    }
}
