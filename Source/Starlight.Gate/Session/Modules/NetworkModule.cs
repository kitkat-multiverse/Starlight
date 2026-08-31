using Google.Protobuf;
using Serilog;
using Starlight.Protobuf.Serialization;
using Starlight.Protocol;
using Starlight.Rpc;

namespace Starlight.Gate.Session.Modules;

public sealed class NetworkModule(INetworkSession session)
{
    public uint ClientTime;

    [Opcode]
    public async Task OnUnionCmdNotify(UnionCmdNotify msg, GamePacket packet)
    {
        foreach (var cmd in msg.CmdList)
        {
            if (cmd.MessageId > ushort.MaxValue)
                continue;

            var body = cmd.Body.ToByteArray();

            Starlight.Protobuf.Core.IMessage message;

            try
            {
                using var stream = new CodedInputStream(body);
                message = session.Registry.Deserialize((int)cmd.MessageId, stream);
            }
            catch (ArgumentOutOfRangeException)
            {
                continue;
            }

            var innerPacket = new GamePacket((ushort)cmd.MessageId, packet.RawMetadata, body);

            if (session.Server.Config.Connections.LogPackets)
            {
                Log.Debug($"[UNION] C>S | Packet: {message.GetType().Name}");
                var jsonObj = JsonSerializer.SerializeToObject(message, session.Registry);
                Log.Debug($"[UNION] {System.Text.Json.JsonSerializer.Serialize(jsonObj)}");
            }

            if (await PacketDispatcher.Dispatch(session, innerPacket, message))
                continue;

            if (session.GameTunnel is {} tunnel)
                await tunnel.Publish(GameSubjects.InboundPacket, message, packet.RawMetadata);
        }
    }

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
