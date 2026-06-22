using Google.Protobuf;
using Serilog;
using Starlight.Protocol;
using Starlight.Gate.Crypto;
using Starlight.Gate.Session.Modules;
using Starlight.Kcp;
using Starlight.Protobuf.Registry;
using Starlight.Rpc.Tunnel;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Gate.Session;

public sealed class StarlightSession : INetworkSession
{
    private static readonly ILogger Logger = Log.ForContext<StarlightSession>();

    private readonly KcpConnection _connection;

    private ProtocolRegistry? _registry;

    public StarlightSession(GateServerService server, KcpConnection connection)
    {
        Server = server;
        _connection = connection;

        XorPad = server.ServerKey;

        Login = new LoginModule(this);
    }

    public GateServerService Server { get; }
    public RpcTunnel? GameTunnel { get; set; }
    public byte[] XorPad { private get; set; }

    public LoginModule Login { get; }

    public async Task HandlePacket(byte[] data)
    {
        #region Pre-process the packet

        CryptoHelper.Xor(data, XorPad);

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

        // Dispatch to a local handler; if handled, we're done.
        if (await PacketDispatcher.Dispatch(this, packet, message))
            return;

        // Otherwise, forward the deserialized message to the game server. The tunnel
        // serializes the POCO once into its own send buffer, so there is no intermediate
        // body copy (no ByteString round-trip through PlayerPacketNotify). The raw metadata
        // bytes ride along as the tunnel header so the game side can reconstruct PacketHead
        // lazily, without the gate parsing it for packets it only forwards.
        if (GameTunnel is { } tunnel)
        {
            await tunnel.Publish(packet.CmdId, message, packet.RawMetadata);
        }
    }

    public void Send(IMessage message, PacketHead? metadata = null)
    {
        var registry = _registry ?? throw new InvalidOperationException(
            "Cannot send a message before the session's protocol version has been resolved.");

        var packet = new GamePacket(registry, message, metadata);
        var bytes = packet.ToBytes();

        if (Server.Config.Connections.LogPackets)
        {
            Logger.Debug("C>S | Packet: {Message} [{CmdId}] ({Length} bytes)",
                message.GetType().Name, packet.CmdId, packet.Body.Length);
        }

        CryptoHelper.Xor(bytes, XorPad);
        _connection.Send(bytes);
    }
}
