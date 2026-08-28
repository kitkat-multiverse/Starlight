using System.Collections.Concurrent;
using Google.Protobuf;
using Starlight.Rpc.Proto;
using Starlight.Rpc.Tunnel.Connection;

namespace Starlight.Rpc.Tunnel;

/// <summary>
/// Game-side helper that accepts incoming tunnel requests from the RPC broadcast layer.
/// </summary>
public sealed class TunnelHost(RpcTransport rpc, ITunnelAcceptor acceptor) : IDisposable
{
    private readonly ConcurrentDictionary<IDisposable, byte> _subs = new();

    /// <summary>
    /// Raised when an incoming tunnel request for a listened subject is accepted.
    /// The local end is ready; attach handlers before the event returns so the peer's
    /// first messages are not dropped.
    /// </summary>
    public event Func<RpcTunnel, NewTunnelReq, Task>? TunnelOpened;

    /// <summary>
    /// Starts listening for tunnel requests targeting <paramref name="subject"/>.
    /// One <see cref="TunnelHost"/> can listen on multiple subjects.
    /// </summary>
    /// <returns>
    /// A handle that stops listening on <paramref name="subject"/> when disposed.
    /// Disposing the <see cref="TunnelHost"/> stops all subjects.
    /// </returns>
    public async Task<IDisposable> Listen(string subject)
    {
        var sub = await rpc.Subscribe<NewTunnelReq>(TunnelSubjects.NewTunnel, async (req, raw) => {
            if (req.Subject != subject) return;

            var (localEnd, meta) = await acceptor.Accept(req);

            // Raise event before replying so handlers are attached before the gate
            // can publish its first message.
            try
            {
                if (TunnelOpened is { } handler)
                    await handler(localEnd, req);
            }
            catch (Exception ex)
            {
                // Avoid leaking acceptor/broker state if user code fails.
                localEnd.Close();
                await raw.Reply(new NewTunnelRsp { Error = $"An exception occurred: {ex.Message}" });
                return;
            }

            await raw.Reply(new NewTunnelRsp { Metadata = ByteString.CopyFrom(meta) });
        });

        _subs[sub] = 0;
        return new Subscription(this, sub);
    }

    public void Dispose()
    {
        foreach (var sub in _subs.Keys)
        {
            sub.Dispose();
        }
        _subs.Clear();
    }

    /// <summary>Idempotent handle returned by <see cref="Listen"/>.</summary>
    private sealed class Subscription(TunnelHost host, IDisposable sub) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, value: 1) != 0) return;

            host._subs.TryRemove(sub, out _);
            sub.Dispose();
        }
    }
}
