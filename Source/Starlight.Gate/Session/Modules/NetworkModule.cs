using Starlight.Protocol;

namespace Starlight.Gate.Session.Modules;

public sealed class NetworkModule(INetworkSession session)
{
    public uint ClientTime;

    [Opcode]
    public void OnPing(PingReq msg, PacketHead header)
    {
        var response = new PingRsp {
            ClientTime = ClientTime = msg.ClientTime,
            Seq = msg.Seq
        };

        var metadata = new PacketHead {
            ClientSequenceId = header.ClientSequenceId
        };

        session.Send(response, metadata);
    }
}
