using Starlight.SDK.Database.Models;

namespace Starlight.SDK.Database;

public interface IAccountRepository
{
    Task<Account?> GetAccountById(uint id);

    /// <summary>Lookup by login name (i.e. the <c>account</c> body field).</summary>
    Task<Account?> GetAccountByUsernameAsync(string username, CancellationToken ct);

    /// <summary>
    /// Lookup by the email address registered against the account. Used by
    /// the ma-passport <c>appLoginByPassword</c> / <c>webLoginByPassword</c>
    /// flows where the client sends an email rather than a username.
    /// </summary>
    Task<Account?> GetAccountByEmailAsync(string email, CancellationToken ct);

    /// <summary>Lookup by the session token previously issued from shield login.</summary>
    Task<Account?> GetAccountBySessionTokenAsync(string token, CancellationToken ct);

    /// <summary>
    /// Creates a brand-new account with the given username and an already-hashed
    /// password, used by the auto-create-on-login flow when
    /// <see cref="SdkConfig.AllowAccountAutoCreate"/> is enabled.
    /// </summary>
    Task<Account> CreateAccountAsync(string username, string passwordHash, CancellationToken ct);

    /// <summary>
    /// Creates a brand-new account keyed by email (rather than username).
    /// Used by the ma-passport <c>appLoginByPassword</c> auto-create flow
    /// when <see cref="SdkConfig.AllowAccountAutoCreate"/>
    /// is enabled and no existing account matches the supplied email.
    /// Both <c>Username</c> and <c>Email</c> columns are populated with
    /// the lowercased email so subsequent lookups by either field work.
    /// </summary>
    Task<Account> CreateAccountFromEmailAsync(string email, string passwordHash, CancellationToken ct);

    /// <summary>
    /// Persists the rotating fields (session token, combo token, known
    /// device ids) after an auth operation succeeds.
    /// </summary>
    Task UpdateSessionAsync(Account account, CancellationToken ct);
}
