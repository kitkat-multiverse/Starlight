using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Starlight.Crypto;
using Starlight.Crypto.Client;
using Starlight.Database;
using Starlight.SDK.Common;
using Starlight.SDK.Database;
using Starlight.SDK.Database.Models;
using Starlight.SDK.Http;

namespace Starlight.SDK.Services;

/// <summary>
/// Default <see cref="IAuthService"/>. Holds the RSA password-decryption key
/// in memory and reads/writes accounts through <see cref="SdkDbContext"/>.
/// </summary>
public sealed class AuthService(
    SdkDbContext db,
    ClientCrypto clientCrypto,
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
            if (!clientCrypto.TryDecryptPassword(password, out var decrypted))
            {
                logger.LogWarning("Failed to RSA-decrypt password for incoming login request");
                return AuthResult.Fail(Retcode.LoginFailed);
            }

            password = decrypted;
        }

        if (sdkConfig.MinPasswordLength > 0 && password.Length < sdkConfig.MinPasswordLength)
            return AuthResult.Fail(Retcode.LoginCancel);

        var record = await db.Accounts.FirstOrDefaultAsync(a => a.Username == account, ct);

        var wasAutoCreated = false;

        if (record is null)
        {
            if (!sdkConfig.AllowAccountAutoCreate)
                return AuthResult.Fail(Retcode.LoginInvalidAccount);

            // TODO: replace with a real registration endpoint, keep auto-create as an opt-in for now
            record = new Account {
                Username = account,
                PasswordHash = Argon2Crypto.Hash(password)
            };
            db.Accounts.Add(record);

            try
            {
                await db.SaveChangesAsync(ct);
                wasAutoCreated = true;
            }
            catch (DbUpdateException ex) when (!ct.IsCancellationRequested && DatabaseErrors.IsUniqueViolation(ex))
            {
                // lost the race, someone else created the account between our read and insert, just pick it up.
                // Only the dead insert gets detached; clearing the tracker would also drop whatever the
                // endpoint sharing this scoped context is still holding on to.
                db.Entry(record).State = EntityState.Detached;
                record = await db.Accounts.FirstOrDefaultAsync(a => a.Username == account, ct);

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

        await db.SaveChangesAsync(ct);
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

        var record = await db.Accounts.FirstOrDefaultAsync(a => a.SessionToken == sessionToken, ct);

        if (record is null)
            return AuthResult.Fail(Retcode.LoginInvalidAccount);

        record.ComboToken = SdkHttpHelpers.GenerateToken(TokenLength);
        record.RegisterDevice(deviceId);

        await db.SaveChangesAsync(ct);
        return AuthResult.Ok(record);
    }
}
