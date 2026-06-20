using System.Text.Json.Serialization;

namespace Starlight.SDK.Http.Models;

/// <summary>
/// Body of <c>POST /hk4e_global/account/ma-passport/api/appLoginByPassword</c>.
/// Both <c>account</c> and <c>password</c> are RSA-encrypted by the client
/// (base64-encoded PKCS#1 v1.5 cipher) and must be decrypted with the
/// server's private key before validation.
/// </summary>
public sealed class MaPassportAppLoginByPasswordRequest
{
    [JsonPropertyName("account")]
    public string? Account { get; set; }

    [JsonPropertyName("password")]
    public string? Password { get; set; }
}

/// <summary>
/// Body of <c>POST /hk4e_global/account/ma-passport/api/appLoginByAuthTicket</c>.
/// The ticket is a one-time token previously issued by an
/// <c>AuthLoginTicket</c> flow (e.g. third-party OAuth completion).
/// </summary>
public sealed class MaPassportAppLoginByAuthTicketRequest
{
    [JsonPropertyName("ticket")]
    public string? Ticket { get; set; }
}

/// <summary>
/// Body of <c>POST /hk4e_global/account/ma-passport/api/reactivateAccount</c>.
/// The action ticket is a one-time token previously issued by the
/// <c>reactivation</c> flow when an account is flagged
/// <see cref="Starlight.SDK.Database.Models.Account.RequireActivation"/>.
/// </summary>
public sealed class MaPassportReactivateAccountRequest
{
    [JsonPropertyName("action_ticket")]
    public string? ActionTicket { get; set; }
}

/// <summary>
/// Payload returned inside <c>ApiResponse.Data</c> for the
/// <c>appLoginByPassword</c>, <c>appLoginByAuthTicket</c>,
/// <c>reactivateAccount</c> and <c>verifySToken</c> endpoints.
/// </summary>
public sealed class MaPassportLoginData
{
    [JsonPropertyName("reactivate_action_ticket")]
    public string ReactivateActionTicket { get; set; } = string.Empty;

    [JsonPropertyName("bind_email_action_ticket")]
    public string BindEmailActionTicket { get; set; } = string.Empty;

    [JsonPropertyName("ext_user_info")]
    public MaPassportExtUserInfo ExtUserInfo { get; set; } = new();

    [JsonPropertyName("token")]
    public MaPassportTokenInfo Token { get; set; } = new();

    [JsonPropertyName("user_info")]
    public MaPassportUserInfo UserInfo { get; set; } = new();
}

public sealed class MaPassportExtUserInfo
{
    [JsonPropertyName("guardian_email")]
    public string GuardianEmail { get; set; } = string.Empty;

    [JsonPropertyName("birth")]
    public string Birth { get; set; } = SdkDefaults.ZeroTimestamp;
}

public sealed class MaPassportTokenInfo
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Token type. Serialised as an integer on the wire — see
    /// <see cref="Starlight.SDK.Common.MaPassportTokenType"/>.
    /// Defaults to <see cref="Starlight.SDK.Common.MaPassportTokenType.GameToken"/>.
    /// </summary>
    [JsonPropertyName("token_type")]
    public MaPassportTokenType TokenType { get; set; } = MaPassportTokenType.GameToken;
}

public sealed class MaPassportUserInfo
{
    [JsonPropertyName("aid")]
    public string Aid { get; set; } = string.Empty;

    [JsonPropertyName("mid")]
    public string Mid { get; set; } = SdkDefaults.DefaultMid;

    [JsonPropertyName("account_name")]
    public string AccountName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("is_email_verify")]
    public int IsEmailVerify { get; set; }

    [JsonPropertyName("area_code")]
    public string AreaCode { get; set; } = string.Empty;

    [JsonPropertyName("mobile")]
    public string Mobile { get; set; } = string.Empty;

    [JsonPropertyName("safe_area_code")]
    public string SafeAreaCode { get; set; } = string.Empty;

    [JsonPropertyName("safe_mobile")]
    public string SafeMobile { get; set; } = string.Empty;

    [JsonPropertyName("realname")]
    public string Realname { get; set; } = string.Empty;

    [JsonPropertyName("identity_code")]
    public string IdentityCode { get; set; } = string.Empty;

    [JsonPropertyName("rebind_area_code")]
    public string RebindAreaCode { get; set; } = string.Empty;

    [JsonPropertyName("rebind_mobile")]
    public string RebindMobile { get; set; } = string.Empty;

    [JsonPropertyName("rebind_mobile_time")]
    public string RebindMobileTime { get; set; } = SdkDefaults.ZeroTimestamp;

    [JsonPropertyName("links")]
    public List<MaPassportAccountLink> Links { get; set; } = new();

    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;

    [JsonPropertyName("password_time")]
    public string PasswordTime { get; set; } = SdkDefaults.ZeroTimestamp;

    [JsonPropertyName("unmasked_email")]
    public string UnmaskedEmail { get; set; } = string.Empty;

    [JsonPropertyName("unmasked_email_type")]
    public int UnmaskedEmailType { get; set; }
}

public sealed class MaPassportAccountLink
{
    [JsonPropertyName("third_party")]
    public string? ThirdParty { get; set; }

    [JsonPropertyName("uid")]
    public string? Uid { get; set; }
}

/// <summary>
/// Payload returned inside <c>ApiResponse.Data</c> for
/// <c>GET /hk4e_global/account/ma-passport/api/getSwitchStatus</c>. Each
/// entry is a feature flag controlling visibility of a UI element on
/// the SDK login screen.
/// </summary>
public sealed class MaPassportSwitchStatusData
{
    [JsonPropertyName("switch_status_map")]
    public Dictionary<string, MaPassportSwitchEntry> SwitchStatusMap { get; set; } = new();
}

public sealed class MaPassportSwitchEntry
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("disabled_versions")]
    public List<string> DisabledVersions { get; set; } = new();
}
