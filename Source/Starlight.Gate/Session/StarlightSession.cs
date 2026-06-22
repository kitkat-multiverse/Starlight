using Google.Protobuf;
using Serilog;
using Starlight.Game.Protocol;
using Starlight.Gate.Crypto;
using Starlight.Gate.Network;
using Starlight.Kcp;
using Starlight.Protobuf.Registry;
using Starlight.Rpc.Tunnel;

namespace Starlight.Gate.Session;

public sealed class StarlightSession : INetworkSession
{
    private static readonly ILogger Logger = Log.ForContext<StarlightSession>();

    private readonly KcpConnection _connection;

    private ProtocolRegistry? _registry;
    private byte[] _xorpad;

    public StarlightSession(GateServerService server, KcpConnection connection)
    {
        Server = server;
        _connection = connection;

        _xorpad = server.ServerKey;
    }

    public GateServerService Server { get; }
    public RpcTunnel? GameTunnel { get; set; }

    public async Task HandlePacket(byte[] data)
    {
        #region Pre-process the packet

        CryptoHelper.Xor(data, _xorpad);

        var packet = new GamePacket(data);

        #endregion

        #region Registry Check & Lookup

        _registry ??= Server.Registry.ResolveByFirstPacket(packet.CmdId)
                      ?? throw new MissingRegistryException(packet.CmdId);

        using var stream = new CodedInputStream(packet.Body);
        var message = _registry.Deserialize(packet.CmdId, stream);

        #endregion

        if (Server.Config.Connections.LogPackets)
        {
            Logger.Debug("C>S | Packet: {Message} [{CmdId}] ({Length} bytes)",
                message.GetType().Name, packet.CmdId, packet.Body.Length);
        }

        // TODO: Handle packets.

        // If the packet handling falls through, forward to the game server.
        if (GameTunnel is { } tunnel)
        {
            var payload = new PlayerPacketNotify {
                Metadata = ByteString.CopyFrom(packet.RawMetadata),
                Payload = ByteString.CopyFrom(packet.Body)
            };
            await tunnel.Publish(packet.CmdId, payload);
        }
    }
}
