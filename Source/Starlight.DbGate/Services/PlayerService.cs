using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Google.Protobuf;
using Starlight.Database;
using Starlight.DbGate.Models;
using Starlight.Rpc;
using Starlight.Rpc.Proto;

namespace Starlight.DbGate.Services;

public sealed class PlayerService(IServiceScopeFactory scopes, ILogger<PlayerService> logger)
{
    /// The <c>minus one</c> is so we can <c>add one</c> in the next code.
    private const uint StartingId = 100_000_000 - 1;

    /// Collisions resolve in a round or two; past that, fail rather than spin.
    private const int MaxInsertAttempts = 5;

    public async Task Fetch(FetchPlayerReq msg, RpcMessage rpc)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StarlightDbContext>();

        var player = await db.Players
            .AsNoTracking()
            .Include(p => p.Profile)
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
        for (var attempt = 0; player is null; attempt++)
        {
            var highestId = await db.Players.MaxAsync(p => (uint?)p.Id) ?? StartingId;

            var candidate = new Player {
                AccountId = msg.AccountUid,
                Id = highestId + 1
            };
            db.Players.Add(candidate);

            try
            {
                await db.SaveChangesAsync();
                player = candidate;
            }
            catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex) && attempt < MaxInsertAttempts)
            {
                db.ChangeTracker.Clear();

                // Null means it was an ID collision rather than a concurrent create, so the
                // loop recomputes the next UID.
                player = await db.Players
                    .AsNoTracking()
                    .Include(p => p.Profile)
                    .FirstOrDefaultAsync(p => p.AccountId == msg.AccountUid);
            }
            catch (DbUpdateException ex)
            {
                logger.LogError(ex, "Failed to create a player for account '{AccountId}'", msg.AccountUid);

                await rpc.Reply(new FetchPlayerRsp { Retcode = StarlightRetcode.ServerError });
                return;
            }
        }

        await rpc.Reply(new FetchPlayerRsp {
            Retcode = StarlightRetcode.Success,
            Player = player.Serialize()
        });
    }

    public async Task Save(SavePlayerReq msg, RpcMessage rpc)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StarlightDbContext>();

        var player = await db.Players.FirstOrDefaultAsync(player => player.Id == msg.Uid);

        if (player is null)
        {
            await rpc.Reply(new SavePlayerRsp { Retcode = StarlightRetcode.PlayerNotFound });
            return;
        }

        try
        {
            player.State = (msg.State ?? new NetPlayerState()).ToByteArray();
            await db.SaveChangesAsync();
            await rpc.Reply(new SavePlayerRsp { Retcode = StarlightRetcode.Success });
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to save player '{PlayerId}'", msg.Uid);
            await rpc.Reply(new SavePlayerRsp { Retcode = StarlightRetcode.ServerError });
        }
    }
}
