using Starlight.Rpc.Proto;
using Starlight.Rpc.Tunnel.Connection;

namespace Starlight.Rpc.Tunnel;

public sealed class DirectTunnelAcceptor(ITunnelBroker broker) : ITunnelAcceptor
{
    public Task<(RpcTunnel localEnd, byte[] replyMetadata)> Accept(NewTunnelReq request)
    {
        var (client, server) = DirectTunnel.CreatePair();
        var handle = broker.Register(client);
        return Task.FromResult((server, handle.ToByteArray()));
    }
}
