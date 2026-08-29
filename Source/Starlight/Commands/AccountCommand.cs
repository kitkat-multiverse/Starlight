using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Starlight.Crypto;
using Starlight.Database;
using Starlight.SDK;
using Starlight.SDK.Database;
using Starlight.SDK.Database.Models;

namespace Starlight.Commands;

public sealed class AccountCommand(
    IServiceScopeFactory scopeFactory,
    SdkConfig sdkConfig
) : ICommand
{
    public string Name => "account";
    public string Description => "Creates or deletes an SDK account.";
    public string Usage => "account <create|delete> <args>";
    public string[] Aliases => [];

    public Task ExecuteAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            LogUsage();
            return Task.CompletedTask;
        }

        return args[0].ToLowerInvariant() switch {
            "create" => CreateAsync(args[1..], cancellationToken),
            "delete" => DeleteAsync(args[1..], cancellationToken),
            _ => UnknownMode(args[0])
        };
    }

    private async Task CreateAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length != 2)
        {
            Log.Warning("Usage: account create <username> <password>");
            return;
        }

        var username = args[0];
        var password = args[1];

        if (string.IsNullOrWhiteSpace(username) || username.Length > Account.MaxUsernameLength)
        {
            Log.Warning("Username must contain 1-{MaxLength} characters.", Account.MaxUsernameLength);
            return;
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < sdkConfig.MinPasswordLength)
        {
            Log.Warning("Password must contain at least {MinLength} characters.", sdkConfig.MinPasswordLength);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SdkDbContext>();

        if (await db.Accounts.AnyAsync(a => a.Username == username, cancellationToken))
        {
            Log.Warning("An account named '{Username}' already exists.", username);
            return;
        }

        var account = new Account {
            Username = username,
            PasswordHash = Argon2Crypto.Hash(password),
            PasswordTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        db.Accounts.Add(account);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (!cancellationToken.IsCancellationRequested &&
                                                   DatabaseErrors.IsUniqueViolation(ex))
        {
            Log.Warning("An account named '{Username}' already exists.", username);
            return;
        }

        Log.Information("Created account '{Username}' with id {AccountId}.", account.Username, account.Id);
    }

    private async Task DeleteAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length != 1)
        {
            Log.Warning("Usage: account delete <id>");
            return;
        }

        if (!uint.TryParse(args[0], out var accountId))
        {
            Log.Warning("Account id must be an unsigned integer.");
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SdkDbContext>();
        var account = await db.Accounts.FindAsync([accountId], cancellationToken);

        if (account is null)
        {
            Log.Warning("Account id {AccountId} does not exist.", accountId);
            return;
        }

        db.Accounts.Remove(account);
        await db.SaveChangesAsync(cancellationToken);

        Log.Information("Deleted account '{Username}' with id {AccountId}.", account.Username, account.Id);
    }

    private static Task UnknownMode(string mode)
    {
        Log.Warning("Unknown account mode '{Mode}'. Expected 'create' or 'delete'.", mode);
        LogUsage();
        return Task.CompletedTask;
    }

    private static void LogUsage()
    {
        Log.Information("Usage: account create <username> <password>");
        Log.Information("       account delete <id>");
    }
}
