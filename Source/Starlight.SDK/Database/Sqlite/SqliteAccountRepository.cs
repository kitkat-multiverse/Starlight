using Starlight.Common;
using Starlight.Database;
using Starlight.SDK.Common;
using Starlight.SDK.Database.Impl.Entities;
using Starlight.SDK.Database.Models;

namespace Starlight.SDK.Database.Impl;

public sealed class SqliteAccountRepository(StarlightDatabase db) : IAccountRepository
{
    /// <summary>
    /// Delimiter used when packing/unpacking
    /// <see cref="AccountEntity.KnownDeviceIds"/> to and from its
    /// pipe-separated string representation.
    /// </summary>
    private const string DeviceIdDelimiter = "|";

    /// <summary>
    /// Maximum length of the username derived from the email during
    /// <see cref="CreateAccountFromEmailAsync"/>. Matches the DB column
    /// limit on <see cref="AccountEntity.Username"/>.
    /// </summary>
    private const int MaxEmailUsernameLength = 64;

    public async Task<Account?> GetAccountById(uint id)
    {
        var entity = await db.FindAsync<AccountEntity>(id);
        return entity is null ? null : Map(entity);
    }

    public async Task<Account?> GetAccountByUsernameAsync(string username, CancellationToken ct)
    {
        var entities = await db.QueryAsync<AccountEntity>(a => a.Username == username, ct);
        var entity = entities.FirstOrDefault();
        return entity is null ? null : Map(entity);
    }

    public async Task<Account?> GetAccountByEmailAsync(string email, CancellationToken ct)
    {
        var lower = email?.ToLowerInvariant();
        var entities = await db.QueryAsync<AccountEntity>(a => a.Email == lower, ct);
        var entity = entities.FirstOrDefault();
        return entity is null ? null : Map(entity);
    }

    public async Task<Account?> GetAccountBySessionTokenAsync(string token, CancellationToken ct)
    {
        var entities = await db.QueryAsync<AccountEntity>(a => a.SessionToken == token, ct);
        var entity = entities.FirstOrDefault();
        return entity is null ? null : Map(entity);
    }

    public async Task<Account> CreateAccountAsync(string username, string passwordHash, CancellationToken ct)
    {
        var entity = new AccountEntity {
            Username = username,
            Password = passwordHash
        };

        db.Add(entity);
        await db.SaveChangesAsync(ct);

        return Map(entity);
    }

    public async Task<Account> CreateAccountFromEmailAsync(string email, string passwordHash, CancellationToken ct)
    {
        var lower = email.ToLowerInvariant();
        var username = lower.Length > MaxEmailUsernameLength ? lower[..MaxEmailUsernameLength] : lower;

        var entity = new AccountEntity {
            Username = username,
            Email = lower,
            Password = passwordHash
        };

        db.Add(entity);
        await db.SaveChangesAsync(ct);

        return Map(entity);
    }

    public async Task UpdateSessionAsync(Account account, CancellationToken ct)
    {
        var entity = await db.FindAsync<AccountEntity>(account.Id, ct);

        if (entity is null)
            throw new InvalidOperationException($"Account {account.Id} not found while updating session state.");

        entity.SessionToken = account.SessionToken;
        entity.ComboToken = account.ComboToken;
        entity.KnownDeviceIds = string.Join(DeviceIdDelimiter, account.KnownDeviceIds);
        entity.Country = account.Country;
        entity.RealNameOperation = account.RealNameOperation;
        entity.RequireRealPerson = account.RequireRealPerson;
        entity.RequireSafeMobile = account.RequireSafeMobile;
        entity.RequireActivation = account.RequireActivation;
        entity.RequireDeviceGrant = account.RequireDeviceGrant;
        entity.AccountType = (int)account.AccountType;
        entity.PasswordTime = account.PasswordTime;

        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    private static Account Map(AccountEntity entity) => new() {
        Id = entity.Id,
        Username = entity.Username,
        Email = entity.Email ?? string.Empty,
        PasswordHash = entity.Password,
        PasswordTime = entity.PasswordTime ?? 0,
        Country = entity.Country ?? string.Empty,
        RealNameOperation = entity.RealNameOperation ?? RealNameOperations.None,
        RequireRealPerson = entity.RequireRealPerson,
        RequireSafeMobile = entity.RequireSafeMobile,
        RequireActivation = entity.RequireActivation,
        RequireDeviceGrant = entity.RequireDeviceGrant,
        AccountType = (AccountType)entity.AccountType,
        SessionToken = entity.SessionToken ?? string.Empty,
        ComboToken = entity.ComboToken ?? string.Empty,
        KnownDeviceIds = string.IsNullOrEmpty(entity.KnownDeviceIds) ?
            [] :
            entity.KnownDeviceIds.Split(DeviceIdDelimiter, StringSplitOptions.RemoveEmptyEntries).ToList()
    };
}
