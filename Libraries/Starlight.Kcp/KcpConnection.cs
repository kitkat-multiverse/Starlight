// KcpConnection.cs

using System.Net;
using Starlight.Kcp.Internals;

namespace Starlight.Kcp;

public sealed class KcpConnection
{
    private readonly KCP _kcp;
    private readonly IKcpServerHandler _handler;
    private readonly Action<byte[], EndPoint> _send; // <-- added

    public EndPoint Remote { get; }
    public int Conv => _kcp.Conv;
    public int Token => _kcp.Token;
    public bool IsDead => _kcp.State == -1;

    internal KcpConnection(int conv, int token, EndPoint remote, IKcpServerHandler handler, Action<byte[], EndPoint> send)
    {
        Remote = remote;
        _handler = handler;
        _send = send;
        _kcp = new KCP(conv, token, stream: false, new WriterAdapter(this));
        _kcp.SetNodelay(nodelay: true, interval: 10, resend: 2, nc: true);
    }

    public void Send(byte[] data) => _kcp.Send(data);

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
        if (IsDead) _handler.OnDisconnected(this);
    }

    private sealed class WriterAdapter(KcpConnection conn) : IWriter
    {
        public void Write(byte[] data) => conn._send(data, conn.Remote); // <-- wired up
    }
}
