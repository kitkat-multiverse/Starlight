using Starlight.SDK.Database.Models;

namespace Starlight.SDK.Database;

public interface IAccountRepository
{
    Task<Account?> GetAccountById(uint id);
}
