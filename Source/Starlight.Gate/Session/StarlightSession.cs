using System.Net;
using Google.Protobuf;
using Serilog;
using Starlight.Common;
using Starlight.Protocol;
using Starlight.Gate.Crypto;
using Starlight.Gate.Session.Modules;
using Starlight.Kcp;
using Starlight.Protobuf.Registry;
using Starlight.Rpc;
using Starlight.Rpc.Tunnel;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Gate.Session;

public sealed class StarlightSession : INetworkSession
{
    private static readonly ILogger Logger = Log.ForContext<StarlightSession>();

    private readonly KcpConnection _connection;

    private uint _sequenceId = 10;
    private ProtocolRegistry? _registry;

    public StarlightSession(GateServerService server, KcpConnection connection)
    {
        Server = server;
        _connection = connection;

        XorPad = server.ServerKey;

        Network = new NetworkModule(this);
        Login = new LoginModule(this);
    }

    public IPEndPoint Remote => _connection.Remote;
    public GateServerService Server { get; }
    public RpcTunnel? GameTunnel { get; set; }
    public byte[] XorPad { private get; set; }

    public NetworkModule Network { get; }
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

        IMessage message;

        try
        {
            using var stream = new CodedInputStream(packet.Body);
            message = _registry.Deserialize(packet.CmdId, stream);
        }
        catch (ArgumentOutOfRangeException)
        {
            Logger.Verbose("C>S | Packet: (obfuscated) [{CmdId}] ({Length})", packet.CmdId, packet.Body.Length);
            return;
        }

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
        if (GameTunnel is {} tunnel)
        {
            await tunnel.Publish(GameSubjects.InboundPacket, message, packet.RawMetadata);
        }
    }

    public void OnClose(uint reason)
    {
        GameTunnel?.Dispose();
    }

    public void Send(IMessage message, PacketHead? metadata = null)
    {
        var registry = _registry ?? throw new InvalidOperationException(
            "Cannot send a message before the session's protocol version has been resolved.");

        metadata ??= new PacketHead();

        if (metadata.ClientSequenceId == 0)
            metadata.ClientSequenceId = ++_sequenceId;

        if (metadata.SentMs == 0)
            metadata.SentMs = Time.CurrentMs();

        var packet = new GamePacket(registry, message, metadata);
        var bytes = packet.ToBytes();

        if (Server.Config.Connections.LogPackets)
        {
            Logger.Debug("S>C | Packet: {Message} [{CmdId}] ({Length} bytes)",
                message.GetType().Name, packet.CmdId, packet.Body.Length);
        }

        CryptoHelper.Xor(bytes, XorPad);
        _connection.Send(bytes);
    }

    public void Disconnect(uint reason, bool flush)
    {
        if (flush)
            _connection.DisconnectAfterFlush(reason);
        else
            _connection.Disconnect(reason);
    }
}
