using Starlight.Gate.Session.Modules;
using Starlight.Protobuf.Registry;
using Starlight.Protocol;
using Starlight.Rpc.Tunnel;
using System.Net;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Gate.Session;

public interface INetworkSession
{
    #region Common Properties

    IPEndPoint Remote { get; }
    GateServerService Server { get; }
    ProtocolRegistry Registry { get; }

    /// The connection to the game server.
    /// <br/>
    /// Packets which are not handled in the gate server are forwarded
    /// to the game server to be processed instead.
    RpcTunnel? GameTunnel { get; }

    /// Fires when the client disconnects. Pass it to anything a handler awaits.
    CancellationToken Closing { get; }

    /// Stages <paramref name="pad"/> as the session's XOR pad.
    /// <br/>
    /// A swap occurs when the current key fails to properly decrypt the packet.
    void Rekey(byte[] pad);

    #endregion

    #region Handler Modules

    NetworkModule Network { get; }
    LoginModule Login { get; }

    #endregion

    #region Lifecycle

    /// Queues an inbound packet. Packets are handled one at a time, in arrival order.
    void Receive(byte[] data);

    /// <inheritdoc cref="StarlightSession.AttachTunnel"/>
    bool AttachTunnel(RpcTunnel tunnel);

    void OnClose(uint reason)
    {}

    #endregion

    /// Sends a message to the connected client, optionally with a <see cref="PacketHead"/>.
    void Send(IMessage message, PacketHead? metadata = null);

    /// Sends after waiting for capacity in the reliable transport's outbound window.
    Task SendAsync(IMessage message, PacketHead? metadata = null);

    /// Drops the client with <paramref name="reason"/>. When <paramref name="flush"/> is set, the
    /// connection lingers until all queued packets are acknowledged so a final packet is delivered.
    void Disconnect(uint reason, bool flush);
}
