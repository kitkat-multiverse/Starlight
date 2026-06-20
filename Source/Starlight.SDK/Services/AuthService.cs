using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Starlight.SDK.Common;
using Starlight.Crypto;
using Starlight.SDK.Database;
using Starlight.SDK.Http;

namespace Starlight.SDK.Services;

/// <summary>
/// Default <see cref="IAuthService"/>. Holds the RSA password-decryption key
/// in memory and delegates all storage operations to
/// <see cref="IAccountRepository"/>.
/// </summary>
public sealed class AuthService(
    IAccountRepository accounts,
    RsaCrypto? passwordCrypto,
    SdkConfig sdkConfig,
    ILogger<AuthService> logger
)
    : IAuthService
{
    private const int TokenLength = 30;

    /// <summary>Matches the DB column limit.</summary>
    private const int MaxAccountLength = 64;

    /// <summary>Matches the DB column / endpoint limits.</summary>
    private const int MaxDeviceIdLength = 128;

    public async Task<AuthResult> LoginAsync(
        string account,
        string password,
        bool isCryptoEncrypted,
        string deviceId,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password)
                                               || string.IsNullOrWhiteSpace(deviceId) || account.Length > MaxAccountLength ||
                                               deviceId.Length > MaxDeviceIdLength)
            return AuthResult.Fail(Retcode.ParameterError);

        // client wraps the password with our RSA public key before sending it
        if (isCryptoEncrypted && !sdkConfig.SkipRsaDecryption)
        {
            if (passwordCrypto is null)
            {
                logger.LogError("Client sent is_crypto=true but no RSA key is configured");
                return AuthResult.Fail(Retcode.SystemError);
            }

            if (!passwordCrypto.TryDecryptPassword(password, out var decrypted))
            {
                logger.LogWarning("Failed to RSA-decrypt password for incoming login request");
                return AuthResult.Fail(Retcode.LoginFailed);
            }

            password = decrypted;
        }

        if (sdkConfig.MinPasswordLength > 0 && password.Length < sdkConfig.MinPasswordLength)
            return AuthResult.Fail(Retcode.LoginCancel);

        var record = await accounts.GetAccountByUsernameAsync(account, ct);

        var wasAutoCreated = false;

        if (record is null)
        {
            if (!sdkConfig.AllowAccountAutoCreate)
                return AuthResult.Fail(Retcode.LoginInvalidAccount);

            // TODO: replace with a real registration endpoint, keep auto-create as an opt-in for now
            try
            {
                record = await accounts.CreateAccountAsync(account, Argon2Crypto.Hash(password), ct);
                wasAutoCreated = true;
            }
            catch (SqliteException ex) when (!ct.IsCancellationRequested && SqliteErrorCodes.IsUniqueConstraintViolation(ex))
            {
                // lost the race, someone else created the account between our read and insert, just pick it up
                record = await accounts.GetAccountByUsernameAsync(account, ct);

                if (record is null)
                    throw;
            }

            record.PasswordTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            logger.LogInformation("Auto-created account id {Id} on first login", record.Id);
        } else if (!Argon2Crypto.Verify(password, record.PasswordHash))
        {
            return AuthResult.Fail(Retcode.LoginInvalidAccount);
        }

        record.SessionToken = SdkHttpHelpers.GenerateToken(TokenLength);
        record.RegisterDevice(deviceId);

        if (wasAutoCreated
            || string.IsNullOrEmpty(record.RealNameOperation)
            || record.RealNameOperation == RealNameOperations.None)
        {
            record.RequireRealPerson = true;
            record.RealNameOperation = RealNameOperations.BindRealname;
        }

        await accounts.UpdateSessionAsync(record, ct);
        return AuthResult.Ok(record);
    }

    public async Task<AuthResult> ExchangeComboTokenAsync(
        string sessionToken,
        string deviceId,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(sessionToken) || string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > MaxDeviceIdLength)
            return AuthResult.Fail(Retcode.ParameterError);

        var record = await accounts.GetAccountBySessionTokenAsync(sessionToken, ct);

        if (record is null)
            return AuthResult.Fail(Retcode.LoginInvalidAccount);

        record.ComboToken = SdkHttpHelpers.GenerateToken(TokenLength);
        record.RegisterDevice(deviceId);

        await accounts.UpdateSessionAsync(record, ct);
        return AuthResult.Ok(record);
    }
}
