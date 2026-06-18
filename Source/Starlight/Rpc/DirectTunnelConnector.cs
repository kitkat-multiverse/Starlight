using Starlight.Rpc.Proto;
using Starlight.Rpc.Tunnel.Connection;

namespace Starlight.Rpc.Tunnel;

public sealed class DirectTunnelConnector(ITunnelBroker broker) : ITunnelConnector
{
    public Task<RpcTunnel> Connect(NewTunnelRsp reply)
    {
        var handle = new Guid(reply.Metadata.Span);
        var tunnel = broker.Claim(handle)
            ?? throw new TunnelHandshakeException($"Unknown tunnel handle '{handle}'.");
        return Task.FromResult(tunnel);
    }
}
