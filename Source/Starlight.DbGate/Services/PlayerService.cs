using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Starlight.DbGate.Models;
using Starlight.Rpc;
using Starlight.Rpc.Proto;

namespace Starlight.DbGate.Services;

public sealed class PlayerService(IServiceScopeFactory scopes)
{
    /// The <c>minus one</c> is so we can <c>add one</c> in the next code.
    private const uint StartingId = 100_000_000 - 1;

    public async Task Fetch(FetchPlayerReq msg, RpcMessage rpc)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StarlightDbContext>();

        var player = await db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.AccountId == msg.AccountUid);

        // Existing player: return them untouched.
        if (player is not null)
        {
            await rpc.Reply(new FetchPlayerRsp {
                Retcode = StarlightRetcode.Success,
                Player = player.Serialize()
            });
            return;
        }

        if (!msg.Create)
        {
            await rpc.Reply(new FetchPlayerRsp { Retcode = StarlightRetcode.PlayerNotFound });
            return;
        }

        // New player: assign the next UID, retrying if a concurrent insert
        // claims the same ID (Id has a unique index).
        while (true)
        {
            var highestId = await db.Players.MaxAsync(p => (uint?)p.Id) ?? StartingId;

            player = new Player {
                AccountId = msg.AccountUid,
                Id = highestId + 1
            };
            db.Players.Add(player);

            try
            {
                await db.SaveChangesAsync();
                break;
            }
            catch (DbUpdateException)
            {
                db.Entry(player).State = EntityState.Detached;

                // A concurrent request may have created this account first.
                var existing = await db.Players
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.AccountId == msg.AccountUid);
                if (existing is null) continue;

                // Otherwise it was an ID collision — recompute and retry.
                player = existing;
                break;
            }
        }

        await rpc.Reply(new FetchPlayerRsp {
            Retcode = StarlightRetcode.Success,
            Player = player.Serialize()
        });
    }
}
