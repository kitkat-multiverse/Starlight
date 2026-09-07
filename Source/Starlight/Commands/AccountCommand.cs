using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    public CommandSource Sources => CommandSource.Console;

    public async Task ExecuteAsync(CommandContext context, string[] args)
    {
        if (args.Length == 0)
        {
            await PrintUsage(context);
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "create":
                await CreateAsync(context, args[1..]);
                break;
            case "delete":
                await DeleteAsync(context, args[1..]);
                break;
            default:
                await context.ReplyAsync(
                    $"Unknown account mode '{args[0]}'. Expected 'create' or 'delete'.",
                    CommandOutputLevel.Warning);
                await PrintUsage(context);
                break;
        }
    }

    private async Task CreateAsync(CommandContext context, string[] args)
    {
        if (args.Length != 2)
        {
            await context.ReplyAsync("Usage: account create <username> <password>", CommandOutputLevel.Warning);
            return;
        }

        var username = args[0];
        var password = args[1];

        if (string.IsNullOrWhiteSpace(username) || username.Length > Account.MaxUsernameLength)
        {
            await context.ReplyAsync(
                $"Username must contain 1-{Account.MaxUsernameLength} characters.",
                CommandOutputLevel.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < sdkConfig.MinPasswordLength ||
            password.Length > sdkConfig.MaPassport.Login.MaxPasswordLength)
        {
            await context.ReplyAsync(
                $"Password must contain at least {sdkConfig.MinPasswordLength} and at most {sdkConfig.MaPassport.Login.MaxPasswordLength} characters.",
                CommandOutputLevel.Warning);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SdkDbContext>();

        if (await db.Accounts.AnyAsync(a => a.Username == username, context.CancellationToken))
        {
            await context.ReplyAsync($"An account named '{username}' already exists.", CommandOutputLevel.Warning);
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
            await db.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException ex) when (!context.CancellationToken.IsCancellationRequested &&
                                           DatabaseErrors.IsUniqueViolation(ex))
        {
            await context.ReplyAsync($"An account named '{username}' already exists.", CommandOutputLevel.Warning);
            return;
        }

        await context.ReplyAsync($"Created account '{account.Username}' with id {account.Id}.");
    }

    private async Task DeleteAsync(CommandContext context, string[] args)
    {
        if (args.Length != 1)
        {
            await context.ReplyAsync("Usage: account delete <id>", CommandOutputLevel.Warning);
            return;
        }

        if (!uint.TryParse(args[0], out var accountId))
        {
            await context.ReplyAsync("Account id must be an unsigned integer.", CommandOutputLevel.Warning);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SdkDbContext>();
        var account = await db.Accounts.FindAsync([accountId], context.CancellationToken);

        if (account is null)
        {
            await context.ReplyAsync($"Account id {accountId} does not exist.", CommandOutputLevel.Warning);
            return;
        }

        db.Accounts.Remove(account);
        await db.SaveChangesAsync(context.CancellationToken);

        await context.ReplyAsync($"Deleted account '{account.Username}' with id {account.Id}.");
    }

    private static async ValueTask PrintUsage(CommandContext context)
    {
        await context.ReplyAsync("Usage: account create <username> <password>");
        await context.ReplyAsync("       account delete <id>");
    }
}
