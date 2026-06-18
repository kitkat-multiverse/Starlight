using Starlight.Rpc.Proto;

namespace Starlight.Rpc.Tunnel.Connection;

/// <summary>
/// Game-side seam: given a <see cref="NewTunnelReq"/>, creates the local end of the tunnel
/// and returns the metadata bytes to embed in <see cref="NewTunnelRsp"/>.
/// <br/>
/// In-memory: creates a linked pair, registers the client end in the broker, returns the handle.
/// Network: binds/allocates a socket and returns the connection info (IP/port/token).
/// </summary>
public interface ITunnelAcceptor
{
    Task<(RpcTunnel localEnd, byte[] replyMetadata)> Accept(NewTunnelReq request);
}
