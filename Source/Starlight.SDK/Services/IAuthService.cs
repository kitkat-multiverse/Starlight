using Starlight.SDK.Common;
using Starlight.SDK.Database.Models;

namespace Starlight.SDK.Services;

/// <summary>
/// Result of an auth operation. Either we succeed and produce an
/// <see cref="Account"/>, or we fail with a structured <see cref="Retcode"/>
/// that the endpoint translates into an <see cref="Starlight.SDK.Http.ApiResponse"/>.
/// </summary>
public readonly record struct AuthResult(Retcode Code, Account? Account)
{
    public bool IsSuccess => Code == Retcode.Success && Account is not null;

    public static AuthResult Ok(Account account) => new(Retcode.Success, account);
    public static AuthResult Fail(Retcode code) => new(code, Account: null);
}

/// <summary>
/// Encapsulates the two-step SDK login flow: validate credentials and gen a
/// session token, then exchange that token for a combo token used by the
/// gate server.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Validates a username/password pair (RSA-decrypting the password if
    /// <paramref name="isCryptoEncrypted"/> is true) and, on success, rotates
    /// the account's session token.
    /// </summary>
    Task<AuthResult> LoginAsync(
        string account,
        string password,
        bool isCryptoEncrypted,
        string deviceId,
        CancellationToken ct
    );

    /// <summary>
    /// Exchanges a session token (from <see cref="LoginAsync"/>) for a combo
    /// token. Issues a new combo token on every successful call.
    /// </summary>
    Task<AuthResult> ExchangeComboTokenAsync(
        string sessionToken,
        string deviceId,
        CancellationToken ct
    );
}
