namespace Starlight.SDK.Common;

/// <summary>
/// Wire-format string keys used in the
/// <see cref="Starlight.SDK.Http.Models.MaPassportSwitchStatusData.SwitchStatusMap"/>
/// returned by <c>/hk4e_global/account/ma-passport/api/getSwitchStatus</c>.
/// </summary>
/// <remarks>
/// These were previously inline string literals in
/// <c>PassportEndpoints.HandleGetSwitchStatus</c>; grouping them here
/// prevents typos from silently disabling a UI affordance on the client.
/// </remarks>
public static class MaPassportSwitchKey
{
    /// <summary>Enables the v2 login UI on Android.</summary>
    public const string UiV2 = "ui_v2";

    /// <summary>Shows the "Sign in with Apple" button.</summary>
    public const string AppleLogin = "apple_login";

    /// <summary>Shows the "Sign in with Google" button.</summary>
    public const string GoogleLogin = "google_login";

    /// <summary>Shows the "Sign in with Twitter" button.</summary>
    public const string TwitterLogin = "twitter_login";

    /// <summary>Shows the "Sign in with Facebook" button.</summary>
    public const string FacebookLogin = "facebook_login";

    /// <summary>Shows the password-login tab on the login screen.</summary>
    public const string PasswordLoginTab = "pwd_login_tab";

    /// <summary>Shows the account-registration tab on the login screen.</summary>
    public const string AccountRegisterTab = "account_register_tab";

    /// <summary>"Reset password" entry on the login screen.</summary>
    public const string PasswordResetEntry = "password_reset_entry";

    /// <summary>"Common questions" entry on the login screen.</summary>
    public const string CommonQuestionEntry = "common_question_entry";

    /// <summary>Bind-email-via-third-party entry.</summary>
    public const string BindUserThirdPartyEmail = "bind_user_thirdparty_email";

    /// <summary>Third-party bind-email entry.</summary>
    public const string ThirdPartyBindEmail = "third_party_bind_email";

    /// <summary>Username-based bind-email entry.</summary>
    public const string UserNameBindEmail = "user_name_bind_email";

    /// <summary>Marketing authorization toggle.</summary>
    public const string MarketingAuthorization = "marketing_authorization";

    /// <summary>Vietnam real-name v1 flow toggle.</summary>
    public const string VietnamRealName = "vn_real_name";

    /// <summary>Vietnam real-name v2 flow toggle.</summary>
    public const string VietnamRealNameV2 = "vn_real_name_v2";

    /// <summary>Firebase unmasked-email gate (always off in Starlight).</summary>
    public const string FirebaseReturnUnmaskedEmail = "firebase_return_unmasked_email";

    /// <summary>Third-party account bind gate (always off in Starlight).</summary>
    public const string BindThirdParty = "bind_thirdparty";
}
