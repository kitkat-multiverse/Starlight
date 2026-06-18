using Starlight.Rpc.Proto;
using Starlight.Rpc.Tunnel.Connection;

namespace Starlight.Rpc.Tunnel;

public sealed class DirectTunnelConnector(ITunnelBroker broker) : ITunnelConnector
{
    public Task<RpcTunnel> Connect(NewTunnelRsp reply)
    {
        if (reply.Metadata.Length != 16)
            throw new TunnelHandshakeException($"Invalid tunnel handle metadata length {reply.Metadata.Length}; expected 16 bytes.");

        var handle = new Guid(reply.Metadata.Span);

        var tunnel = broker.Claim(handle)
                     ?? throw new TunnelHandshakeException($"Unknown tunnel handle '{handle}'.");
        return Task.FromResult(tunnel);
    }
}
