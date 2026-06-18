using System.Collections.Concurrent;
using Starlight.Common;

namespace Starlight.Rpc.Tunnel;

public sealed class DirectTunnelBroker : ITunnelBroker
{
    private readonly ConcurrentDictionary<Guid, RpcTunnel> _pending = new();

    public Guid Register(RpcTunnel clientEnd)
    {
        var id = Random.Shared.NextUuid();
        _pending[id] = clientEnd;
        return id;
    }

    public RpcTunnel? Claim(Guid handle)
        => _pending.TryRemove(handle, out var tunnel) ? tunnel : null;
}
