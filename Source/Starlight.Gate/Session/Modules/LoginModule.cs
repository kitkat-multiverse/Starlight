using Starlight.Protocol;
using Starlight.Protobuf.Core;
using Starlight.Rpc;

namespace Starlight.Gate.Session.Modules;

public sealed class LoginModule(INetworkSession session)
{
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(5);

    // [Opcode(typeof(GetPlayerTokenReq))]
    public async Task<IMessage> OnGetPlayerTokenReq(GetPlayerTokenReq msg, PacketHead header)
    {
        // TODO: Pick better server based on population and load.
        session.GameTunnel = await session.Server.Tunnel.Open(GameSubjects.GateConnection, reqTimeout: ReplyTimeout);

        // ... do whatever
        return new GetPlayerTokenRsp {
            Uid = 4
        };
    }
}
