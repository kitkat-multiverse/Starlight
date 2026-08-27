using Microsoft.EntityFrameworkCore;
using Starlight.SDK.Database.Models;

namespace Starlight.SDK.Database;

public sealed class SdkDbContext(DbContextOptions<SdkDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts { get; set; } = null!;
}
