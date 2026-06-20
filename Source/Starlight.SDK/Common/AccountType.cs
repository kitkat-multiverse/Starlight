namespace Starlight.SDK.Common;

/// <summary>
/// Account types reported in
/// <see cref="Starlight.SDK.Database.Models.Account.AccountType"/> and
/// echoed back in
/// <see cref="Starlight.SDK.Http.Models.ComboGranterLoginResponse.AccountType"/>.
/// The wire format is the integer value.
/// </summary>
public enum AccountType
{
    /// <summary>
    /// Anonymous / guest account created on first login without
    /// credentials. Has limited capabilities and is upgraded to
    /// <see cref="Normal"/> on first credential bind.
    /// </summary>
    Guest = 0,

    /// <summary>
    /// Standard credential-backed account. Default for newly created
    /// accounts that came in through username/password or email login.
    /// </summary>
    Normal = 1
}
