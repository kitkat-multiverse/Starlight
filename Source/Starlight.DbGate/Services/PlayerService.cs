using Microsoft.Extensions.DependencyInjection;
using Starlight.Rpc;
using Starlight.Rpc.Proto;

namespace Starlight.DbGate.Services;

public sealed class PlayerService(IServiceScopeFactory scopes)
{
    public async Task Fetch(FetchPlayerReq msg, RpcMessage rpc)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StarlightDbContext>();

        var player = await db.Players.FindAsync(msg.Uid);

        await rpc.Reply(new FetchPlayerRsp {
            Retcode = player is null ? StarlightRetcode.PlayerNotFound : StarlightRetcode.Success,
            Player = player?.Serialize()
        });
    }
}
