using Google.Protobuf;
using Starlight.Common;
using Starlight.Rpc;
using Starlight.Rpc.Proto;
using Starlight.Rpc.Tunnel.Connection;

namespace Starlight.Rpc.Tunnel;

/// <summary>
/// Gate-side helper that opens a tunnel to a game server via the existing RPC broadcast layer.
/// </summary>
public sealed class TunnelClient(RpcTransport rpc, ITunnelConnector connector)
{
    /// <summary>
    /// Announces a new tunnel request on <c>rpc.tunnel</c> and connects to the first (or best) responder.
    /// </summary>
    /// <param name="subject">The tunnel subject the requester wants (matched by the acceptor).</param>
    /// <param name="metadata">Optional opaque bytes sent to the acceptor.</param>
    /// <param name="timeout">
    ///   For the first-reply path: how long to wait before giving up.<br/>
    ///   For the sorted path: how long to collect replies before picking the winner.
    /// </param>
    /// <param name="sorter">
    ///   Optional. When provided, all replies received within <paramref name="timeout"/> are
    ///   collected, then <paramref name="sorter"/> selects the winner. The list is never empty
    ///   when <paramref name="sorter"/> is called.<br/>
    ///   When null, first-reply-wins semantics apply (faster; stops waiting immediately).
    /// </param>
    public async Task<RpcTunnel> Open(
        string subject,
        byte[]? metadata = null,
        TimeSpan? timeout = null,
        Func<IReadOnlyList<NewTunnelRsp>, NewTunnelRsp>? sorter = null
    )
    {
        var req = new NewTunnelReq {
            Subject = subject,
            Metadata = ByteString.CopyFrom(metadata ?? [])
        };

        if (sorter is not null)
            return await OpenWithSorter(req, sorter, timeout ?? TimeSpan.FromSeconds(5));

        var rsp = await rpc.Request<NewTunnelReq, NewTunnelRsp>(
            TunnelSubjects.NewTunnel, req, timeout);
        return await connector.Connect(rsp);
    }

    private async Task<RpcTunnel> OpenWithSorter(
        NewTunnelReq req,
        Func<IReadOnlyList<NewTunnelRsp>, NewTunnelRsp> sorter,
        TimeSpan window
    )
    {
        var replySubject = $"reply_{Random.Shared.NextUuid()}";

        var reqMsg = new RpcMessage(req.ToByteArray()) {
            ReplySubject = replySubject,
            Transport = rpc
        };

        var replies = new List<NewTunnelRsp>();

        var sub = await rpc.Subscribe(replySubject, msg => {
            if (msg.TryDeserialize<NewTunnelRsp>() is not {} rsp)
                return Task.CompletedTask;

            lock (replies)
            {
                replies.Add(rsp);
            }
            return Task.CompletedTask;
        });

        await rpc.Publish(TunnelSubjects.NewTunnel, reqMsg);
        await Task.Delay(window);
        sub.Dispose();

        if (replies.Count == 0)
            throw new TunnelHandshakeException($"No server responded to tunnel subject '{req.Subject}'.");

        var winner = sorter(replies.AsReadOnly());
        return await connector.Connect(winner);
    }
}
