using System.Net;
using System.Net.Sockets;

namespace Starlight.Kcp;

public sealed class KcpServer : IDisposable
{
    private readonly UdpClient _socket;
    private readonly IKcpServerHandler _handler;
    private readonly Dictionary<(int Conv, int Token), KcpConnection> _connections = new();
    private readonly CancellationTokenSource _cts = new();

    public KcpServer(string address, int port, IKcpServerHandler handler)
    {
        var endpoint = new IPEndPoint(
            IPAddress.Parse(address),
            port);

        _socket = new UdpClient(endpoint);
        _handler = handler;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var receiveLoop = ReceiveLoopAsync(_cts.Token);
        var updateLoop = UpdateLoopAsync(_cts.Token);
        await Task.WhenAll(receiveLoop, updateLoop);
    }

    public void Stop() => _cts.Cancel();

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var result = await _socket.ReceiveAsync(ct);
            HandlePacket(result.Buffer, result.RemoteEndPoint);
        }
    }

    private async Task UpdateLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        while (await timer.WaitForNextTickAsync(ct))
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var (key, conn) in _connections)
            {
                conn.Update(now);
                if (conn.IsDead) _connections.Remove(key);
            }
        }
    }

    private void HandlePacket(byte[] data, IPEndPoint remote)
    {
        if (data.Length < 8) return;

        var conv  = BitConverter.ToInt32(data, 0);
        var token = BitConverter.ToInt32(data, 4);
        var key   = (conv, token);

        if (!_connections.TryGetValue(key, out var conn))
        {
            conn = new KcpConnection(conv, token, remote, _handler, SendTo);
            _connections[key] = conn;
            _handler.OnConnected(conn);
        }

        conn.Input(data);
    }

    private void SendTo(byte[] data, EndPoint remote)
    {
        var ep = (IPEndPoint)remote;
        _socket.Send(data, ep);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _socket.Dispose();
    }
}