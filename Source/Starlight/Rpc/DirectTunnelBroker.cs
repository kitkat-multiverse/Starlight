using System.Collections.Concurrent;
using Starlight.Common;

namespace Starlight.Rpc.Tunnel;

public sealed class DirectTunnelBroker : ITunnelBroker
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<Guid, Pending> _pending = new();

    /// <param name="ttl">
    /// How long an unclaimed end may sit before it is closed and evicted. Must comfortably exceed
    /// the longest decision window a requester might use (its <c>collectWindow</c>/<c>reqTimeout</c>),
    /// otherwise the winning end can be reaped before it is claimed.
    /// </param>
    public DirectTunnelBroker(TimeSpan? ttl = null)
        => _ttl = ttl ?? DefaultTtl;

    public Guid Register(RpcTunnel clientEnd)
    {
        var id = Random.Shared.NextUuid();
        var entry = new Pending(clientEnd);
        while (!_pending.TryAdd(id, entry))
            id = Random.Shared.NextUuid();

        clientEnd.OnClosed += () => Evict(id);

        // Armed last so the timer callback never observes a half-built entry.
        entry.Timer = new Timer(_ => Expire(id), null, _ttl, Timeout.InfiniteTimeSpan);

        return id;
    }

    public RpcTunnel? Claim(Guid handle)
    {
        if (!_pending.TryRemove(handle, out var entry)) return null;
        entry.Timer?.Dispose();
        return entry.Tunnel;
    }

    // The atomic TryRemove is the arbiter between a claim and an expiry: whoever removes the
    // entry owns the outcome, so a tunnel claimed at the instant its TTL fires is never closed.
    private void Expire(Guid id)
    {
        if (!_pending.TryRemove(id, out var entry)) return;
        entry.Timer?.Dispose();
        // Closing cascades to the peer; the OnClosed handler's Evict then no-ops (already removed).
        entry.Tunnel.Close();
    }

    private void Evict(Guid id)
    {
        if (_pending.TryRemove(id, out var entry))
            entry.Timer?.Dispose();
    }

    private sealed class Pending(RpcTunnel tunnel)
    {
        public RpcTunnel Tunnel { get; } = tunnel;
        public Timer? Timer { get; set; }
    }
}
