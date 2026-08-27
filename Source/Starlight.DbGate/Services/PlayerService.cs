using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    /// SQLITE_CONSTRAINT_PRIMARYKEY & SQLITE_CONSTRAINT_UNIQUE.
    private const int SqlitePrimaryKey = 1555;
    private const int SqliteUnique = 2067;

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
            catch (DbUpdateException ex) when (IsUniqueViolation(ex) && attempt < MaxInsertAttempts)
            {
                db.Entry(candidate).State = EntityState.Detached;

                // Null means it was an ID collision rather than a concurrent create, so the
                // loop recomputes the next UID.
                player = await db.Players
                    .AsNoTracking()
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

    /// Anything else (NOT NULL, I/O, disk full) would retry forever, since the AccountId
    /// lookup that ends the loop only finds rows a concurrent insert created.
    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is SqliteException {
            SqliteExtendedErrorCode: SqlitePrimaryKey or SqliteUnique
        };
}
