using System.Collections.Frozen;

namespace Starlight.SDK.Common;

public static class RetcodeMessages
{
    private static readonly FrozenDictionary<Retcode, string> Map =
        new Dictionary<Retcode, string> {
            [Retcode.Success] = "OK",
            [Retcode.Fail] = "Failed",
            [Retcode.Cancel] = "Cancelled",
            [Retcode.ParameterError] = "Invalid parameter",
            [Retcode.SystemError] = "System error",
            [Retcode.LoginCancel] = "System request failed, please go back and retry",
            [Retcode.LoginFailed] = "Login failed, please try again",
            [Retcode.LoginNetworkAtRisk] = "Request failed: current network environment is at risk",
            [Retcode.LoginInvalidAccount] = "Account not found",
            [Retcode.MissingConfiguration] = "Signature error",
            [Retcode.WrongAccount] = "Wrong account",
            [Retcode.ProtocolFailed] = "Protocol failed",

            [Retcode.NoSuchMethod] = "No such method",
            [Retcode.BbsNotLogin] = "Session expired, please log in again",
            [Retcode.GrantInvalidCode] = "Invalid grant code",
            [Retcode.GranterUnknownError] = "Unknown granter error",
            [Retcode.InvalidJsonBody] = "Invalid JSON body",

            [Retcode.NeedRealname] = "Real-name verification required",
            [Retcode.NeedGuardian] = "Guardian verification required",

            [Retcode.PayFailed] = "Payment failed",
            [Retcode.PayCancel] = "Payment cancelled",
            [Retcode.PayError] = "Payment error",
            [Retcode.PayUnknown] = "Unknown payment error",
            [Retcode.EmptyPaymentToken] = "Empty payment token",
            [Retcode.InvalidPaymentToken] = "Invalid payment token",

            [Retcode.ExpiredCaptchaVerification] = "Captcha verification expired",

            [Retcode.MaPassportSystemError] = "Passport system error",
            [Retcode.MaPassportParameterError] = "Passport parameter error",
            [Retcode.MaPassportAccountFormatError] = "Account format error",
            [Retcode.MaPassportTicketNotExist] = "Ticket does not exist",
            [Retcode.MaPassportPasswordFormatError] = "Password format error",
            [Retcode.MaPassportIllegalParameter] = "Illegal parameter",
            [Retcode.MaPassportAccountNotExist] = "Account does not exist",
            [Retcode.MaPassportCaptchaMismatch] = "Captcha mismatch",
            [Retcode.MaPassportAccountMismatch] = "Account mismatch",
            [Retcode.MaPassportAccountNewDeviceDetected] = "New device detected, please verify your identity",
            [Retcode.MaPassportQrCodeExpired] = "QR code expired",

            [Retcode.BindRealPersonErrorParameters] = "Real-name binding parameter error",
            [Retcode.BindRealPersonInvalidTicket] = "Real-name binding ticket invalid",

            [Retcode.LauncherNotFound] = "Launcher not found",

            [Retcode.ComboInvalidKey] = "RetCode_InvalidKey",
            [Retcode.ComboPlatformNoConfig] = "RetCode_NoConfig"
        }.ToFrozenDictionary();

    public static string Get(Retcode code) =>
        Map.TryGetValue(code, out var msg) ? msg : $"Error ({(int)code})";
}
