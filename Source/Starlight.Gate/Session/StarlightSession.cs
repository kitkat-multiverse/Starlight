using Google.Protobuf;
using Serilog;
using Starlight.Common;
using Starlight.Gate.Crypto;
using Starlight.Gate.Session.Modules;
using Starlight.Kcp;
using Starlight.Protobuf.Registry;
using Starlight.Protobuf.Serialization;
using Starlight.Protocol;
using Starlight.Rpc;
using Starlight.Rpc.Tunnel;
using System.Net;
using System.Threading.Channels;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Gate.Session;

public sealed class StarlightSession : INetworkSession
{
    private static readonly ILogger Logger = Log.ForContext<StarlightSession>();

    /// A client that outruns <see cref="ConsumeInbound"/> gets dropped rather than growing
    /// the queue until the process runs out of memory.
    private const int MaxQueuedPackets = 1024;
    private const int MaxPendingSendSegments = 32;

    private readonly KcpConnection _connection;

    /// Restores arrival order, which the KCP callback loses by dispatching each packet
    /// separately. Without it a packet can decrypt with the pad the login swapped in behind it.
    private readonly Channel<byte[]> _inbound = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(MaxQueuedPackets) {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.Wait
    });

    private readonly CancellationTokenSource _closing = new();

    /// Handlers aren't the only callers of <see cref="Send"/>; the game tunnel's relay
    /// subscriptions call it off their own tasks.
    private readonly Lock _sendLock = new();

    private byte[] _xorPad;

    /// The pad the client moves to once it has processed GetPlayerTokenRsp. Staged rather than
    /// applied, because until that lands the client is still both encrypting and decrypting
    /// with the old one, and a reply under the new pad is unreadable to it.
    private byte[]? _pendingPad;

    private uint _sequenceId = 10;
    private ProtocolRegistry? _registry;

    public StarlightSession(GateServerService server, KcpConnection connection)
    {
        Server = server;
        _connection = connection;

        _xorPad = server.ServerKey;

        Network = new NetworkModule(this);
        Login = new LoginModule(this);

        _ = Task.Run(ConsumeInbound);
    }

    public IPEndPoint Remote => _connection.Remote;
    public GateServerService Server { get; }
    public ProtocolRegistry Registry => _registry
                                        ?? throw new InvalidOperationException(
                                            "Cannot access the protocol registry before the session's protocol version has been resolved.");
    public RpcTunnel? GameTunnel { get; private set; }
    public CancellationToken Closing => _closing.Token;

    /// <inheritdoc/>
    public void Rekey(byte[] pad)
    {
        lock (_sendLock)
        {
            _pendingPad = pad;
        }
    }

    public NetworkModule Network { get; }
    public LoginModule Login { get; }

    public void Receive(byte[] data)
    {
        if (_inbound.Writer.TryWrite(data))
            return;

        Logger.Warning("Dropping {Remote}: more than {Max} packets queued.", Remote, MaxQueuedPackets);
        Disconnect((uint)DisconnectReason.PacketFreqTooHigh, flush: false);
    }

    /// Swaps in the tunnel to the game server. Returns false when the session closed while the
    /// tunnel was being opened, in which case the caller must abandon what it was doing.
    public bool AttachTunnel(RpcTunnel tunnel)
    {
        lock (_sendLock)
        {
            if (!_closing.IsCancellationRequested)
            {
                // A client can re-handshake on a live connection; the old tunnel's subscriptions
                // would keep relaying into this session otherwise.
                GameTunnel?.Dispose();
                GameTunnel = tunnel;
                return true;
            }
        }

        tunnel.Dispose();
        return false;
    }

    private async Task ConsumeInbound()
    {
        try
        {
            await foreach (var data in _inbound.Reader.ReadAllAsync(_closing.Token))
            {
                try
                {
                    await HandlePacket(data);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Logger.Warning(ex, "Failed to handle packet for {Remote}", Remote);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The session closed; whatever is still queued is no longer worth handling.
        }
    }

    private async Task HandlePacket(byte[] data)
    {
        #region Pre-process the packet

        CryptoHelper.Xor(data, _xorPad);

        // A packet the current pad can't make sense of is how the client tells us it finished
        // the login handshake; everything after it, replies included, rides the staged pad.
        if (_pendingPad is {} pending && !GamePacket.HasValidHeader(data))
        {
            CryptoHelper.Xor(data, _xorPad); // back to the raw bytes; XOR is its own inverse
            CryptoHelper.Xor(data, pending);

            if (GamePacket.HasValidHeader(data))
            {
                lock (_sendLock)
                {
                    _xorPad = pending;
                    _pendingPad = null;
                }
            } else
            {
                // Neither pad fits, so this is corruption rather than the rekey. Undo the
                // guess so the parse below reports what the live pad actually produced.
                CryptoHelper.Xor(data, pending);
                CryptoHelper.Xor(data, _xorPad);
            }
        }

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
            var jsonObj = JsonSerializer.SerializeToObject(message, _registry);
            Logger.Debug($"{System.Text.Json.JsonSerializer.Serialize(jsonObj)}");
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
        _closing.Cancel();
        _inbound.Writer.TryComplete();

        // Under the same lock AttachTunnel takes, so a login still awaiting Tunnel.Open can't
        // hand us a tunnel after this point.
        lock (_sendLock)
        {
            GameTunnel?.Dispose();
            GameTunnel = null;
        }
    }

    public void Send(IMessage message, PacketHead? metadata = null)
    {
        var registry = _registry ?? throw new InvalidOperationException(
            "Cannot send a message before the session's protocol version has been resolved.");

        lock (_sendLock)
        {
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
                var jsonObj = JsonSerializer.SerializeToObject(message, _registry);
                Logger.Debug($"{System.Text.Json.JsonSerializer.Serialize(jsonObj)}");
            }

            CryptoHelper.Xor(bytes, _xorPad);
            _connection.Send(bytes);
        }
    }

    public async Task SendAsync(IMessage message, PacketHead? metadata = null)
    {
        await _connection.WaitForSendCapacityAsync(MaxPendingSendSegments, Closing);
        Send(message, metadata);
    }

    public void Disconnect(uint reason, bool flush)
    {
        if (flush)
            _connection.DisconnectAfterFlush(reason);
        else
            _connection.Disconnect(reason);
    }
}
