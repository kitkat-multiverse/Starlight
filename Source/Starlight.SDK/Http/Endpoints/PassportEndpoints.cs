using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Starlight.SDK.Common;
using Starlight.Crypto;
using Starlight.SDK.Database;
using Starlight.SDK.Database.Models;
using Starlight.SDK.Http.Models;
using Starlight.SDK.Services;

namespace Starlight.SDK.Http.Endpoints;

/// <summary>
/// Implements the <c>/hk4e_global/account/ma-passport/api/**</c> endpoints.
/// Currently covers:
/// <list type="bullet">
///   <item><c>POST getConfig</c></item>
///   <item><c>POST appLoginByPassword</c></item>
///   <item><c>POST appLoginByAuthTicket</c></item>
///   <item><c>POST reactivateAccount</c></item>
///   <item><c>GET  getSwitchStatus</c></item>
/// </list>
/// </summary>
public static class PassportEndpoints
{
    private static readonly string[] PathPrefixes =
        ["/hk4e_global/account/ma-passport/api", "/hk4e_cn/account/ma-passport/api", "/account/ma-passport/api"];

    public static void MapPassportEndpoints(this IEndpointRouteBuilder routes)
    {
        foreach (var prefix in PathPrefixes)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                continue;

            routes.MapPost($"{prefix}/getConfig", HandleGetConfig);

            // Login flows. The three POST endpoints share a common response
            // shape (MaPassportLoginData) and a common helper for building
            // the user_info block.
            routes.MapPost($"{prefix}/appLoginByPassword", HandleAppLoginByPasswordAsync);
            routes.MapPost($"{prefix}/appLoginByAuthTicket", HandleAppLoginByAuthTicketAsync);
            routes.MapPost($"{prefix}/reactivateAccount", HandleReactivateAccountAsync);

            // Per-platform UI feature flags for the SDK login screen.
            routes.MapGet($"{prefix}/getSwitchStatus", HandleGetSwitchStatus);
        }
    }

    private static async Task<IResult> HandleGetConfig(
        HttpContext httpContext,
        [FromServices] SdkConfig sdkConfig,
        [FromServices] IGeoIpLookup geoIp,
        CancellationToken ct
    )
    {
        var remoteIp = SdkHttpHelpers.GetClientIp(httpContext);
        var countryCode = await geoIp.GetCountryCodeAsync(remoteIp, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(countryCode))
            countryCode = sdkConfig.DefaultCountryCode;

        var areaCode = SdkHttpHelpers.CountryToMobileCode(countryCode);

        return Results.Ok(ApiResponse.Ok(new MaPassportConfigData {
            Ip = new MaPassportIpInfo {
                CountryCode = countryCode,
                Language = sdkConfig.MaPassport.Language,
                AreaCode = areaCode
            },
            AreaWhitelist = sdkConfig.MaPassport.AreaWhitelist,
            RealnameWhitelist = sdkConfig.MaPassport.RealnameWhitelist,
            GuardianAgeLimit = sdkConfig.MaPassport.GuardianAgeLimit,
            DisableMmt = sdkConfig.MaPassport.DisableMmt,
            ShowBirthday = sdkConfig.MaPassport.ShowBirthday.ToString().ToLowerInvariant()
        }));
    }

    /// <summary>
    /// Handles <c>POST /hk4e_global/account/ma-passport/api/appLoginByPassword</c>.
    /// Authenticates an account using email + RSA-encrypted password and
    /// returns a fresh game token plus the masked user-info block.
    /// </summary>
    private static async Task<IResult> HandleAppLoginByPasswordAsync(
        HttpContext httpContext,
        [FromBody] MaPassportAppLoginByPasswordRequest? body,
        [FromHeader(Name = "x-rpc-device_id")] string? deviceId,
        [FromServices] SdkConfig sdkConfig,
        [FromServices] IGeoIpLookup geoIp,
        [FromServices] IAccountRepository accounts,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct
    )
    {
        // A static type can't be a generic type argument (CS0718), so
        // ILogger<T> isn't an option here. The factory caches by category,
        // so this lookup is cheap.
        var logger = loggerFactory.CreateLogger(typeof(PassportEndpoints).FullName!);

        if (body is null || string.IsNullOrEmpty(body.Account) || string.IsNullOrEmpty(body.Password))
            return Results.Ok(ApiResponse.From(Retcode.MaPassportParameterError));

        if (string.IsNullOrEmpty(deviceId) || !SdkUtils.IsValidDeviceId(deviceId))
            return Results.Ok(ApiResponse.From(Retcode.MaPassportParameterError));

        string account;
        string password;

        if (sdkConfig.MaPassport.Login.SkipRsaDecryption)
        {
            account = body.Account!;
            password = body.Password!;
        } else
        {
            var passwordCrypto = httpContext.RequestServices.GetService<RsaCrypto>();

            if (passwordCrypto is null)
            {
                logger.LogDebug("appLoginByPassword: no RSA key configured but SkipRsaDecryption=false");
                return Results.Ok(ApiResponse.From(Retcode.MaPassportSystemError));
            }

            if (!passwordCrypto.TryDecryptPassword(body.Account!, out account)
                || !passwordCrypto.TryDecryptPassword(body.Password!, out password))
            {
                return Results.Ok(ApiResponse.From(Retcode.MaPassportSystemError));
            }
        }

        if (!account.Contains('@'))
            return Results.Ok(ApiResponse.From(Retcode.MaPassportAccountFormatError));

        // TODO: ma-passport also supports a username-based login flow
        // (distinct from the email flow above). We currently reject any
        // non-email identifier, once the username login path is wired up,
        // branch here on a username-shape check rather than failing.

        var login = sdkConfig.MaPassport.Login;

        if (password.Length < login.MinPasswordLength || password.Length > login.MaxPasswordLength)
            return Results.Ok(ApiResponse.From(Retcode.MaPassportPasswordFormatError));

        var acc = await accounts.GetAccountByEmailAsync(account, ct)
                  ?? await accounts.GetAccountByUsernameAsync(account, ct);

        var wasAutoCreated = false;

        if (acc is null)
        {
            if (!sdkConfig.AllowAccountAutoCreate)
                return Results.Ok(ApiResponse.From(Retcode.MaPassportAccountMismatch));

            try
            {
                acc = await accounts.CreateAccountFromEmailAsync(account, Argon2Crypto.Hash(password), ct);
                wasAutoCreated = true;
            }
            catch (SqliteException ex) when (!ct.IsCancellationRequested && SqliteErrorCodes.IsUniqueConstraintViolation(ex))
            {
                acc = await accounts.GetAccountByEmailAsync(account, ct)
                      ?? await accounts.GetAccountByUsernameAsync(account, ct);

                if (acc is null)
                    throw;
            }

            acc.PasswordTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            logger.LogInformation("Auto-created account id {Id} on ma-passport login", acc.Id);
        } else if (!Argon2Crypto.Verify(password, acc.PasswordHash))
        {
            return Results.Ok(ApiResponse.From(Retcode.MaPassportAccountMismatch));
        }

        if (wasAutoCreated
            || string.IsNullOrEmpty(acc.RealNameOperation)
            || acc.RealNameOperation == RealNameOperations.None)
        {
            acc.RequireRealPerson = true;
            acc.RealNameOperation = RealNameOperations.BindRealname;
        }

        if (!login.SkipDeviceIdCheck
            && acc.KnownDeviceIds.Count > 0
            && !acc.KnownDeviceIds.Contains(deviceId))
        {
            return Results.Ok(ApiResponse.From(Retcode.MaPassportAccountNewDeviceDetected));
        }

        // Issue a fresh session token and persist.
        acc.SessionToken = SdkHttpHelpers.GenerateToken(login.TokenLength);
        acc.RegisterDevice(deviceId);

        if (string.IsNullOrEmpty(acc.Country))
            acc.Country = await geoIp.GetCountryCodeAsync(SdkHttpHelpers.GetClientIp(httpContext), ct).ConfigureAwait(false);

        await accounts.UpdateSessionAsync(acc, ct);

        return Results.Ok(ApiResponse.Ok(BuildLoginData(
            acc,
            (MaPassportTokenType)login.AppLoginTokenType,
            acc.SessionToken,
            string.Empty,
            string.Empty,
            acc.Country)));
    }

    /// <summary>
    /// Handles <c>POST /hk4e_global/account/ma-passport/api/appLoginByAuthTicket</c>.
    /// Exchanges a one-time <c>AuthLoginTicket</c> for an stoken. The
    /// ticket is consumed (single-use).
    /// </summary>
    /// <remarks>
    /// Starlight does not yet persist tickets in its database, so this
    /// endpoint returns <see cref="Retcode.MaPassportIllegalParameter"/>
    /// for any non-empty ticket. The plumbing is in place; the only thing
    /// missing is a <c>ITicketRepository</c> wired through
    /// <see cref="IAccountRepository"/>. TODO: implement once ticket
    /// storage is added.
    /// </remarks>
    private static Task<IResult> HandleAppLoginByAuthTicketAsync(
        [FromBody] MaPassportAppLoginByAuthTicketRequest? body
    )
    {
        if (body is null || string.IsNullOrEmpty(body.Ticket))
            return Task.FromResult(Results.Ok(ApiResponse.From(Retcode.MaPassportParameterError)));

        // TODO: look up the ticket in the ticket repository, resolve the
        // account, and delete the ticket (single-use). For now we return
        // an explicit "illegal parameter" so the client surfaces the
        // right error rather than hanging.
        //
        // Once a ticket repository exists, the implementation should:
        //   1. Resolve the ticket to an account id.
        //   2. Load the account via accounts.GetAccountById(id, ct).
        //   3. Resolve the caller's country via geoIp.
        //   4. Return BuildLoginData(acc, AuthTicketTokenType, acc.SessionToken, ...).
        return Task.FromResult(Results.Ok(ApiResponse.From(Retcode.MaPassportIllegalParameter)));
    }

    /// <summary>
    /// Handles <c>POST /hk4e_global/account/ma-passport/api/reactivateAccount</c>.
    /// Consumes a one-time <c>reactivation</c> action ticket and clears
    /// <see cref="Account.RequireActivation"/> on the associated account.
    /// </summary>
    /// <remarks>
    /// Same caveat as <see cref="HandleAppLoginByAuthTicketAsync"/>:
    /// ticket persistence isn't wired up yet. The plumbing is in place
    /// so this can be flipped on once a ticket repository exists.
    /// </remarks>
    private static Task<IResult> HandleReactivateAccountAsync(
        [FromBody] MaPassportReactivateAccountRequest? body
    )
    {
        if (body is null || string.IsNullOrEmpty(body.ActionTicket))
            return Task.FromResult(Results.Ok(ApiResponse.From(Retcode.MaPassportParameterError)));

        // TODO: look up the ticket in the ticket repository and resolve
        // the account id, then clear RequireActivation.
        //
        // Once a ticket repository exists, the implementation should:
        //   1. Resolve the ticket to an account id.
        //   2. Load the account via accounts.GetAccountById(id, ct).
        //   3. Set acc.RequireActivation = false; await accounts.UpdateSessionAsync(acc, ct).
        //   4. Resolve the caller's country via geoIp.
        //   5. Return BuildLoginData(acc, AppLoginTokenType, acc.SessionToken, ...).
        return Task.FromResult(Results.Ok(ApiResponse.From(Retcode.MaPassportTicketNotExist)));
    }

    /// <summary>
    /// Handles <c>GET /hk4e_global/account/ma-passport/api/getSwitchStatus</c>.
    /// Returns the per-platform UI feature flags for the SDK login screen.
    /// </summary>
    private static IResult HandleGetSwitchStatus(
        [FromQuery] string? app_id,
        [FromQuery] int? platform,
        [FromServices] SdkConfig sdkConfig
    )
    {
        if (string.IsNullOrEmpty(app_id) || platform is null)
            return Results.Ok(ApiResponse.From(Retcode.MaPassportParameterError));

        var s = sdkConfig.MaPassport.Login;
        var switchMap = new Dictionary<string, MaPassportSwitchEntry>();

        // ui_v2 is only enabled on Android.
        if (platform == (int)PlatformId.Android && s.EnableAndroidUiV2)
        {
            switchMap[MaPassportSwitchKey.UiV2] = NewEntry(true);
        }

        // Third-party login options.
        if (s.EnableThirdPartyLogins)
        {
            switchMap[MaPassportSwitchKey.AppleLogin] = NewEntry(true);
            switchMap[MaPassportSwitchKey.GoogleLogin] = NewEntry(true);
            switchMap[MaPassportSwitchKey.TwitterLogin] = NewEntry(true);
            switchMap[MaPassportSwitchKey.FacebookLogin] = NewEntry(true);
        }

        // Login/register tabs.
        if (s.EnableLoginRegisterTabs)
        {
            switchMap[MaPassportSwitchKey.PasswordLoginTab] = NewEntry(true);
            switchMap[MaPassportSwitchKey.AccountRegisterTab] = NewEntry(true);
        }

        // Always-on UI affordances.
        switchMap[MaPassportSwitchKey.PasswordResetEntry] = NewEntry(true);
        switchMap[MaPassportSwitchKey.CommonQuestionEntry] = NewEntry(true);
        switchMap[MaPassportSwitchKey.BindUserThirdPartyEmail] = NewEntry(true);
        switchMap[MaPassportSwitchKey.ThirdPartyBindEmail] = NewEntry(true);
        switchMap[MaPassportSwitchKey.UserNameBindEmail] = NewEntry(true);
        switchMap[MaPassportSwitchKey.MarketingAuthorization] = NewEntry(true);

        // Always-off / Vietnam-only switches.
        switchMap[MaPassportSwitchKey.VietnamRealName] = NewEntry(s.EnableVietnamRealName);
        switchMap[MaPassportSwitchKey.VietnamRealNameV2] = NewEntry(s.EnableVietnamRealName);
        switchMap[MaPassportSwitchKey.FirebaseReturnUnmaskedEmail] = NewEntry(false);
        switchMap[MaPassportSwitchKey.BindThirdParty] = NewEntry(false);

        return Results.Ok(ApiResponse.Ok(new MaPassportSwitchStatusData {
            SwitchStatusMap = switchMap
        }));

        static MaPassportSwitchEntry NewEntry(bool enabled)
        {
            return new MaPassportSwitchEntry { Enabled = enabled };
        }
    }

    /// <summary>
    /// Builds the common login response payload shared by
    /// <c>appLoginByPassword</c>, <c>appLoginByAuthTicket</c>,
    /// <c>reactivateAccount</c> and <c>verifySToken</c>.
    /// </summary>
    private static MaPassportLoginData BuildLoginData(
        Account acc,
        MaPassportTokenType tokenType,
        string token,
        string reactivateActionTicket,
        string bindEmailActionTicket,
        string country
    ) => new() {
        ReactivateActionTicket = reactivateActionTicket,
        BindEmailActionTicket = bindEmailActionTicket,
        ExtUserInfo = new MaPassportExtUserInfo {
            GuardianEmail = string.Empty,
            Birth = SdkDefaults.ZeroTimestamp
        },
        Token = new MaPassportTokenInfo {
            Token = token,
            TokenType = tokenType
        },
        UserInfo = new MaPassportUserInfo {
            Aid = acc.Id.ToString(),
            Mid = acc.Id.ToString(),
            AccountName = acc.Username,
            Email = SdkHttpHelpers.MaskString(acc.Email),
            IsEmailVerify = 0,
            AreaCode = string.Empty,
            Mobile = string.Empty,
            SafeAreaCode = string.Empty,
            SafeMobile = string.Empty,
            Realname = string.Empty,
            IdentityCode = string.Empty,
            RebindAreaCode = string.Empty,
            RebindMobile = string.Empty,
            RebindMobileTime = SdkDefaults.ZeroTimestamp,
            Links = [],
            Country = country,
            PasswordTime = acc.PasswordTime > 0 ? acc.PasswordTime.ToString() : SdkDefaults.ZeroTimestamp,
            UnmaskedEmail = string.Empty,
            UnmaskedEmailType = 0
        }
    };
}
