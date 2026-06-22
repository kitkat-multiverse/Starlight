using Starlight.Rpc.Tunnel;

namespace Starlight.Gate.Session;

public interface INetworkSession
{
    #region Common Properties

    GateServerService Server { get; }

    /// The connection to the game server.
    /// <br/>
    /// Packets which are not handled in the gate server are forwarded
    /// to the game server to be processed instead.
    RpcTunnel? GameTunnel { get; set; }

    #endregion

    #region Lifecycle

    Task HandlePacket(byte[] data);

    void OnClose(uint reason)
    {
    }

    #endregion
}
