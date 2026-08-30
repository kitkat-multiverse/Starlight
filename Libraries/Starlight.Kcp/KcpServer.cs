using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Starlight.Kcp;

public sealed class KcpServer : IDisposable
{
    private readonly UdpClient _socket;
    private readonly IKcpServerHandler _handler;
    private readonly ConcurrentDictionary<(uint Conv, uint Token), KcpConnection> _connections = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly LogDelegate _logger;

    public KcpServer(string address, int port, LogDelegate logger, IKcpServerHandler handler)
    {
        var endpoint = new IPEndPoint(
            IPAddress.Parse(address),
            port);

        _socket = new UdpClient(endpoint);
        _handler = handler;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);

        var receive = ReceiveLoopAsync(linked.Token);
        var update = UpdateLoopAsync(linked.Token);

        try
        {
            await await Task.WhenAny(receive, update);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the normal shutdown path.
        }
        finally
        {
            await linked.CancelAsync();

            // Let the surviving loop observe the cancellation before its token source goes away.
            try
            {
                await Task.WhenAll(receive, update);
            }
            catch
            {
                // Whatever took the server down already came out of the await above.
            }

            DisconnectAll();
        }
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

            foreach (var conn in _connections.Values)
            {
                conn.Update(now);
            }
        }
    }

    private void HandlePacket(byte[] data, IPEndPoint remote)
    {
        switch (data.Length)
        {
            case < 8:
                return;
            case 20:
                HandleHandshake(data, remote);
                return;
        }

        var conv = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan()[..4]);
        var token = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var key = (conv, token);

        if (!_connections.TryGetValue(key, out var conn))
        {
            _logger(LogLevel.Verbose, "Received packet from {Remote} before establishing connection. (conv={ConvId}, token={Token})",
                remote, conv, token);
            return;
        }

        conn.Input(data);
    }

    private void HandleHandshake(byte[] data, IPEndPoint remote)
    {
        switch (Handshake.Parse(data))
        {
            case ConnectHandshake:
                uint convId, token;

                do
                {
                    convId = (uint)Random.Shared.Next(minValue: 0, int.MaxValue);
                    token = (uint)Random.Shared.Next(minValue: 0, int.MaxValue);
                } while (_connections.ContainsKey((convId, token)));

                var conn = new KcpConnection(convId, token, remote, _handler, SendTo, FinalizeDisconnect);
                _connections.TryAdd((convId, token), conn);

                var reply = new ExchangeHandshake(convId, token);
                SendTo(reply.ToByteArray(), remote);

                _handler.OnConnected(conn);
                break;
            case DisconnectHandshake hs:
                if (_connections.TryGetValue((hs.ConvId, hs.Token), out var existing) && existing.Remote.Equals(remote))
                    FinalizeDisconnect(existing, (uint)hs.Reason);
                break;
            case ExchangeHandshake:
                _logger(LogLevel.Verbose, "Received unexpected 'Exchange' type handshake from {Remote}.", remote);
                return;
            default:
                _logger(LogLevel.Verbose, "Received invalid handshake from {Remote}.", remote);
                return;
        }
    }

    private void FinalizeDisconnect(KcpConnection conn, uint reason)
    {
        var pair = new KeyValuePair<(uint Conv, uint Token), KcpConnection>(
            (conn.Conv, conn.Token), conn);

        if (!((ICollection<KeyValuePair<(uint Conv, uint Token), KcpConnection>>)_connections).Remove(pair))
            return;

        _handler.OnDisconnected(conn, reason);
    }

    private void DisconnectAll()
    {
        foreach (var conn in _connections.Values)
            FinalizeDisconnect(conn, (uint)DisconnectReason.ServerKillClient);
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
        DisconnectAll();
    }
}
