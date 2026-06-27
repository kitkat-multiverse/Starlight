using System.Net;
using Starlight.Kcp.Internals;

namespace Starlight.Kcp;

public sealed class KcpConnection
{
    private readonly Internals.Kcp _kcp;
    private readonly IKcpServerHandler _handler;
    private readonly Action<byte[], EndPoint> _send;
    private readonly Action<KcpConnection, uint> _onDisconnect;

    private uint? _lingerReason;

    public IPEndPoint Remote { get; }
    public uint Conv => _kcp.Conv;
    public uint Token => _kcp.Token;
    public bool IsDead => _kcp.State == -1;

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

    public void Send(byte[] data) => _kcp.Send(data);

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
        var result = _kcp.Input(new ByteCursor(data));
        if (result.IsFailure) return;

        var buf = new byte[65536];

        while (true)
        {
            var recv = _kcp.Recv(buf);
            if (recv.IsFailure) break;

            _handler.OnReceive(this, buf[..recv.Value]);
        }
    }

    internal void Update(long timestamp)
    {
        _kcp.Update(timestamp);

        // A pending graceful disconnect fires once the send buffers have drained,
        // i.e. the client has acked everything we queued before the kick.
        if (_lingerReason is {} reason && _kcp.SndQueue.Count == 0 && _kcp.SndBuf.Count == 0)
        {
            _lingerReason = null;
            Disconnect(reason);
            return;
        }

        if (IsDead) _handler.OnDisconnected(this, (uint)DisconnectReason.ServerKillClient);
    }

    private sealed class WriterAdapter(KcpConnection conn) : IWriter
    {
        public void Write(byte[] data) => conn._send(data, conn.Remote); // <-- wired up
    }
}
