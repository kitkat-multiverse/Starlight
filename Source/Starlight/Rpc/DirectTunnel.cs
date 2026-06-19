using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Rpc.Tunnel;

/// <summary>
/// Zero-copy in-memory tunnel end. Two ends are linked via <see cref="CreatePair"/>;
/// publishing on one end delivers to the other's subscribers.
/// </summary>
public sealed class DirectTunnel : RpcTunnel
{
    private DirectTunnel _peer = null!;
    private readonly Dictionary<int, List<AsyncTunnelHandler>> _intHandlers = new();
    private readonly Dictionary<string, List<AsyncTunnelHandler>> _stringHandlers = new();

    private DirectTunnel()
    {
    }

    /// <summary>Creates two linked tunnel ends. Publish on one delivers to the other.</summary>
    public static (RpcTunnel client, RpcTunnel server) CreatePair()
    {
        var a = new DirectTunnel();
        var b = new DirectTunnel();
        a._peer = b;
        b._peer = a;
        return (a, b);
    }

    protected override TunnelMessage Serialize(IMessage message) => new DirectTunnelMessage(message);

    public override IDisposable Subscribe(int id, AsyncTunnelHandler handler)
        => Add(_intHandlers, id, handler);

    public override IDisposable Subscribe(string id, AsyncTunnelHandler handler)
        => Add(_stringHandlers, id, handler);

    public override Task Publish(int id, TunnelMessage message)
    {
        // Capture peer before the closed check so a concurrent Close() that nulls _peer
        // cannot produce an NRE. A message queued this way may still reach the peer even
        // if Close() wins the race after ThrowIfClosed() — that window is intentional
        // (delivery-in-flight is preferable to a silent drop or a hard throw mid-send).
        var peer = _peer;
        ThrowIfClosed();
        return peer.Deliver(peer._intHandlers, id, message);
    }

    public override Task Publish(string id, TunnelMessage message)
    {
        var peer = _peer;
        ThrowIfClosed();
        return peer.Deliver(peer._stringHandlers, id, message);
    }

    protected override void OnSelfClosed()
    {
        _intHandlers.Clear();
        _stringHandlers.Clear();
        _peer = null!;
    }

    protected override void NotifyPeerClosed() => _peer.MarkClosedFromPeer();

    private static IDisposable Add<TKey>(
        Dictionary<TKey, List<AsyncTunnelHandler>> map,
        TKey key,
        AsyncTunnelHandler handler
    ) where TKey : notnull
    {
        lock (map)
        {
            if (!map.TryGetValue(key, out var list))
                map[key] = list = [];
            list.Add(handler);
        }
        return new Subscription<TKey>(map, key, handler);
    }

    private async Task Deliver<TKey>(
        Dictionary<TKey, List<AsyncTunnelHandler>> map,
        TKey key,
        TunnelMessage message
    ) where TKey : notnull
    {
        // Stamp the receiving end so Reply() routes back to the original sender
        // via this end's peer, rather than the sender stamped at Serialize time.
        BindReceiver(message, this);

        List<AsyncTunnelHandler>? snapshot = null;

        lock (map)
        {
            if (map.TryGetValue(key, out var list))
                snapshot = [.. list];
        }

        if (snapshot is null) return;

        foreach (var handler in snapshot)
        {
            await handler(message);
        }
    }

    private sealed class Subscription<TKey>(
        Dictionary<TKey, List<AsyncTunnelHandler>> map,
        TKey key,
        AsyncTunnelHandler handler
    ) : IDisposable where TKey : notnull
    {
        public void Dispose()
        {
            lock (map)
            {
                if (!map.TryGetValue(key, out var list)) return;

                list.Remove(handler);
                if (list.Count == 0) map.Remove(key);
            }
        }
    }
}

/// <summary>Zero-copy message wrapper: stashes the live <see cref="IMessage"/> in <see cref="TunnelMessage.Metadata"/>.</summary>
public sealed class DirectTunnelMessage : TunnelMessage
{
    public DirectTunnelMessage(IMessage message)
    {
        Metadata = message;
    }

    public override T? TryDecode<T>() where T : class => Metadata as T;

    public override IMessage? TryDecode(Type type)
        => Metadata is IMessage msg && type.IsInstanceOfType(msg) ? msg : null;
}
