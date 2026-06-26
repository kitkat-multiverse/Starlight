using System.Net;
using Starlight.Gate.Session.Modules;
using Starlight.Protocol;
using Starlight.Rpc.Tunnel;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Gate.Session;

public interface INetworkSession
{
    #region Common Properties

    IPEndPoint Remote { get; }
    GateServerService Server { get; }

    /// The connection to the game server.
    /// <br/>
    /// Packets which are not handled in the gate server are forwarded
    /// to the game server to be processed instead.
    RpcTunnel? GameTunnel { get; set; }

    byte[] XorPad { set; }

    #endregion

    #region Handler Modules

    LoginModule Login { get; }

    #endregion

    #region Lifecycle

    Task HandlePacket(byte[] data);

    void OnClose(uint reason)
    {
    }

    #endregion

    /// Sends a message to the connected client, optionally with a <see cref="PacketHead"/>.
    void Send(IMessage message, PacketHead? metadata = null);

    /// Drops the client with <paramref name="reason"/>. When <paramref name="flush"/> is set, the
    /// connection lingers until all queued packets are acknowledged so a final packet is delivered.
    void Disconnect(uint reason, bool flush);
}
