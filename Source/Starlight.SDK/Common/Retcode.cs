namespace Starlight.SDK.Common;

/// <summary>
/// SDK retcode set.
/// </summary>
public enum Retcode
{
    Success = 0,
    Fail = -1,
    Cancel = -2,

    NoSuchMethod = -10,
    BbsNotLogin = -100,
    SystemError = -101,
    LoginCancel = -102,
    ParameterError = -103,
    MissingConfiguration = -104,
    WrongAccount = -105,
    ProtocolFailed = -106,
    PayFailed = -107,
    PayCancel = -108,
    PayError = -109,
    LoginFailed = -111,
    PayUnknown = -116,
    NeedRealname = -118,
    NeedGuardian = -119,
    LoginNetworkAtRisk = -115,
    GrantInvalidCode = -201,
    LoginInvalidAccount = -216,
    ExpiredCaptchaVerification = -276,
    GranterUnknownError = -400,
    InvalidJsonBody = -502,
    LauncherNotFound = -1200,

    ComboInvalidKey = 6,
    ComboPlatformNoConfig = 7,

    MaPassportSystemError = -3000,
    MaPassportParameterError = -3001,
    MaPassportAccountFormatError = -3002,
    MaPassportTicketNotExist = -3003,
    MaPassportPasswordFormatError = -3004,
    MaPassportIllegalParameter = -3005,
    MaPassportAccountNotExist = -3203,
    MaPassportCaptchaMismatch = -3205,
    MaPassportAccountMismatch = -3208,
    MaPassportAccountNewDeviceDetected = -3239,
    MaPassportQrCodeExpired = -3501,

    EmptyPaymentToken = -7000,
    InvalidPaymentToken = -7001,

    BindRealPersonErrorParameters = -200001,
    BindRealPersonInvalidTicket = -200005
}
