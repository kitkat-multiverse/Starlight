using System.Collections.Concurrent;
using Starlight.Common;

namespace Starlight.Rpc.Tunnel;

public sealed class DirectTunnelBroker : ITunnelBroker
{
    private readonly ConcurrentDictionary<Guid, RpcTunnel> _pending = new();

    public Guid Register(RpcTunnel clientEnd)
    {
        var id = Random.Shared.NextUuid();
        while (!_pending.TryAdd(id, clientEnd))
            id = Random.Shared.NextUuid();

        clientEnd.OnClosed += () => _pending.TryRemove(id, out _);

        return id;
    }

    public RpcTunnel? Claim(Guid handle)
        => _pending.TryRemove(handle, out var tunnel) ? tunnel : null;
}
