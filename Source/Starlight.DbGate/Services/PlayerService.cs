using Starlight.Rpc;
using Starlight.Rpc.Proto;

namespace Starlight.DbGate.Services;

public sealed class PlayerService(StarlightDbContext db)
{
    public async Task Fetch(FetchPlayerReq msg, RpcMessage rpc)
    {
        var player = await db.Players.FindAsync(msg.Uid);

        await rpc.Reply(new FetchPlayerRsp {
            Retcode = player is null ? StarlightRetcode.PlayerNotFound : StarlightRetcode.Success,
            Player = player?.Serialize()
        });
    }
}
