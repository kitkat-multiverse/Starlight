using System.Net;
using Starlight.Kcp.Internals;

namespace Starlight.Kcp;

public sealed class KcpConnection
{
    private const long DeadLinkGraceMilliseconds = 1000;

    private readonly Internals.Kcp _kcp;

    /// The KCP state is reached from three threads -- the socket receive loop, the 10ms update
    /// loop, and whichever thread happens to be sending -- and none of its queues are safe to
    /// touch concurrently. Handler callbacks stay outside it: they reach back into the session,
    /// which takes a lock of its own before calling <see cref="Send"/>.
    private readonly Lock _gate = new();

    private readonly IKcpServerHandler _handler;
    private readonly Action<byte[], EndPoint> _send;
    private readonly Action<KcpConnection, uint> _onDisconnect;

    private uint? _lingerReason;
    private long? _deadLinkSince;
    private bool _isDead;

    public IPEndPoint Remote { get; }
    public uint Conv => _kcp.Conv;
    public uint Token => _kcp.Token;

    /// <summary>
    /// True once an unacknowledged dead-link condition has survived the recovery grace period.
    /// KCP can hit its retransmit threshold during a burst just before the delayed ACK arrives.
    /// </summary>
    public bool IsDead
    {
        get
        {
            lock (_gate)
            {
                return _isDead;
            }
        }
    }

    /// <summary>The number of outbound segments waiting to be sent or acknowledged.</summary>
    public int PendingSendSegments
    {
        get
        {
            lock (_gate)
            {
                return _kcp.SndQueue.Count + _kcp.SndBuf.Count;
            }
        }
    }

    internal KcpConnection(
        uint conv,
        uint token,
        IPEndPoint remote,
        IKcpServerHandler handler,
        Action<byte[], EndPoint> send,
        Action<KcpConnection, uint> onDisconnect
    )
    {
        Remote = remote;
        _handler = handler;
        _send = send;
        _onDisconnect = onDisconnect;
        _kcp = new Internals.Kcp(conv, token, stream: false, new WriterAdapter(this));
        _kcp.SetNodelay(nodelay: true, interval: 10, resend: 2, nc: true);
    }

    public void Send(byte[] data)
    {
        lock (_gate)
        {
            _kcp.Send(data);
            FlushNow();
        }
    }

    /// <summary>
    /// Waits until KCP has room for another application packet. This keeps bulk producers from
    /// building an unbounded send queue while the peer is still acknowledging an earlier window.
    /// </summary>
    public async Task WaitForSendCapacityAsync(int maxPendingSegments, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPendingSegments, other: 1);

        // TODO: replace polling with signal-based wakeup
        while (PendingSendSegments >= maxPendingSegments)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(millisecondsDelay: 10, ct);
        }
    }

    /// Pushes whatever KCP has queued -- pending ACKs and outbound segments both -- instead of
    /// letting it wait for the next update tick, which on Windows lands ~15ms out rather than
    /// the 10 we ask for. The peer smooths its displayed ping from how long our ACKs take to
    /// come back, so a tick's worth of delay here shows up as latency in-game.
    /// Refreshing Current first keeps Flush from stamping resend timers off the last tick's
    /// clock. Flush refuses until the first Update has run; the tick covers that window.
    private void FlushNow()
    {
        _kcp.Current = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _kcp.Flush();
    }

    public void Disconnect(DisconnectReason reason) => Disconnect((uint)reason);

    public void Disconnect(uint reason = (uint)DisconnectReason.ServerKick)
    {
        var hs = new DisconnectHandshake(Conv, Token, reason);
        _send(hs.ToByteArray(), Remote);
        _onDisconnect(this, reason);
    }

    /// <summary>
    /// Disconnects only once every queued segment has been acknowledged by the client.
    /// Use when a packet sent just before teardown must be guaranteed to arrive, since
    /// <see cref="Disconnect(uint)"/> drops anything still in flight.
    /// </summary>
    public void DisconnectAfterFlush(uint reason = (uint)DisconnectReason.ServerKick) => _lingerReason = reason;

    internal void Input(byte[] data)
    {
        List<byte[]>? received = null;

        lock (_gate)
        {
            var result = _kcp.Input(new ByteCursor(data));
            if (result.IsFailure) return;

            // Input only queues the ACKs for what just arrived; nothing sends them.
            FlushNow();

            var buf = new byte[65536];

            while (true)
            {
                var recv = _kcp.Recv(buf);
                if (recv.IsFailure) break;

                (received ??= []).Add(buf[..recv.Value]);
            }
        }

        foreach (var packet in received ?? [])
        {
            _handler.OnReceive(this, packet);
        }
    }

    internal void Update(long timestamp)
    {
        uint? drained = null;
        var disconnected = false;

        lock (_gate)
        {
            _kcp.Update(timestamp);

            if (_kcp.State == -1)
            {
                var hasDeadSegment = _kcp.SndBuf.Any(segment => segment.Xmit >= _kcp.DeadLink);

                if (!hasDeadSegment)
                {
                    // The segment which reached DeadLink was acknowledged before the grace
                    // period expired. Other packets may still be queued, but the link is alive.
                    _kcp.State = 0;
                    _deadLinkSince = null;
                } else
                {
                    _deadLinkSince ??= timestamp;

                    if (timestamp - _deadLinkSince.Value >= DeadLinkGraceMilliseconds)
                    {
                        _isDead = true;
                        disconnected = true;
                    }
                }
            } else
            {
                _deadLinkSince = null;
            }

            // A pending graceful disconnect fires once the send buffers have drained,
            // i.e. the client has acked everything we queued before the kick.
            if (_lingerReason is {} reason && _kcp.SndQueue.Count == 0 && _kcp.SndBuf.Count == 0)
            {
                _lingerReason = null;
                drained = reason;
            }
        }

        if (drained is {} flushed)
        {
            Disconnect(flushed);
            return;
        }

        if (disconnected)
            _handler.OnDisconnected(this, (uint)DisconnectReason.ServerKillClient);
    }

    private sealed class WriterAdapter(KcpConnection conn) : IWriter
    {
        public void Write(byte[] data) => conn._send(data, conn.Remote); // <-- wired up
    }
}
