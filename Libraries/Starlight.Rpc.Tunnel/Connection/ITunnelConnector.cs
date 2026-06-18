using Starlight.Rpc.Proto;

namespace Starlight.Rpc.Tunnel.Connection;

/// <summary>
/// Gate-side seam: given a <see cref="NewTunnelRsp"/>, produces the local end of the tunnel.
/// <br/>
/// In-memory: decodes the broker handle from <c>Metadata</c> and claims the registered end.
/// Network: dials the remote endpoint described in <c>Metadata</c> (IP/port/token).
/// </summary>
public interface ITunnelConnector
{
    Task<RpcTunnel> Connect(NewTunnelRsp reply);
}
