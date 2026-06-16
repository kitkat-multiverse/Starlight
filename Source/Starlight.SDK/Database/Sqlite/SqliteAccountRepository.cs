using Starlight.Database;
using Starlight.SDK.Database.Impl.Entities;
using Starlight.SDK.Database.Models;

namespace Starlight.SDK.Database.Impl;

public sealed class SqliteAccountRepository(StarlightDatabase db) : IAccountRepository
{
    public async Task<Account?> GetAccountById(uint id)
    {
        var entity = await db.FindAsync<AccountEntity>(id);
        if (entity is null) return null;

        return new Account {
            Id = entity.Id,
            Username = entity.Username,
            Email = entity.Email ?? string.Empty,
            Password = entity.Password,
            PasswordTime = 0
        };
    }
}
