using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Starlight.Rpc;
using Starlight.Rpc.Proto;
using Starlight.SDK.Database;

namespace Starlight.SDK;

public sealed class AccountRpcService(
    RpcTransport rpc,
    IServiceScopeFactory scopeFactory,
    SdkConfig config
) : IHostedService
{
    private IDisposable? _subscription;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _subscription = await rpc.Subscribe<ValidateAccountReq>(
            SdkSubjects.ValidateAccount,
            ValidateAccount);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        return Task.CompletedTask;
    }

    private async Task ValidateAccount(ValidateAccountReq request, RpcMessage message)
    {
        if (!uint.TryParse(request.AccountId, out var accountId))
        {
            await message.Reply(new ValidateAccountRsp {
                Retcode = StarlightRetcode.AccountNotFound
            });
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SdkDbContext>();

        // Only retrieve the two values needed by the gate, and do not track an entity
        // which this read-only request will never update.
        var account = await db.Accounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => new { a.ComboToken, a.Country })
            .FirstOrDefaultAsync();

        var retcode = account switch {
            null => StarlightRetcode.AccountNotFound,
            _ when !TokensMatch(account.ComboToken, request.AccountToken)
                => StarlightRetcode.AccountInvalidToken,
            _ => StarlightRetcode.Success
        };

        var response = new ValidateAccountRsp { Retcode = retcode };

        if (retcode == StarlightRetcode.Success)
        {
            response.CountryCode = string.IsNullOrWhiteSpace(account!.Country) ? config.DefaultCountryCode : account.Country;
        }

        await message.Reply(response);
    }

    private static bool TokensMatch(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);

        return expectedBytes.Length == actualBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
