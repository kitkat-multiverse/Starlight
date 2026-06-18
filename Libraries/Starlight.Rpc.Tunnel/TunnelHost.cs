using Google.Protobuf;
using Starlight.Rpc;
using Starlight.Rpc.Proto;
using Starlight.Rpc.Tunnel.Connection;

namespace Starlight.Rpc.Tunnel;

/// <summary>
/// Game-side helper that accepts incoming tunnel requests from the RPC broadcast layer.
/// </summary>
public sealed class TunnelHost(RpcTransport rpc, ITunnelAcceptor acceptor) : IDisposable
{
    private readonly HashSet<IDisposable> _subs = [];

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
    public async Task Listen(string subject)
    {
        _subs.Add(await rpc.Subscribe<NewTunnelReq>(TunnelSubjects.NewTunnel, async (req, raw) => {
            if (req.Subject != subject) return;

            var (localEnd, meta) = await acceptor.Accept(req);

            // Raise event before replying so handlers are attached before the gate
            // can publish its first message.
            if (TunnelOpened is { } handler)
                await handler(localEnd, req);

            await raw.Reply(new NewTunnelRsp { Metadata = ByteString.CopyFrom(meta) });
        }));
    }

    public void Dispose()
    {
        foreach (var sub in _subs) sub.Dispose();
        _subs.Clear();
    }
}
