using Google.Protobuf;
using Serilog;
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
    /// <param name="reqTimeout">For the first-reply path: how long to wait before giving up.</param>
    /// <param name="collectWindow">For the sorted path: how long to collect replies before picking the winner.</param>
    /// <param name="sorter">
    ///   Optional. When provided, all replies received within <paramref name="collectWindow"/> are
    ///   collected, then <paramref name="sorter"/> selects the winner. The list is never empty
    ///   when <paramref name="sorter"/> is called.<br/>
    ///   When null, first-reply-wins semantics apply (faster; stops waiting immediately).
    /// </param>
    /// <param name="ct">Cancellation token when using the sorted path.</param>
    public async Task<RpcTunnel> Open(
        string subject,
        byte[]? metadata = null,
        TimeSpan? reqTimeout = null,
        TimeSpan? collectWindow = null,
        Func<IReadOnlyList<NewTunnelRsp>, NewTunnelRsp>? sorter = null,
        CancellationToken ct = default
    )
    {
        var req = new NewTunnelReq {
            Subject = subject,
            Metadata = ByteString.CopyFrom(metadata ?? [])
        };

        if (sorter is not null)
        {
            if (collectWindow is not {} collectWd)
            {
                throw new ArgumentNullException(nameof(collectWindow), "A collect window must be provided when using a sorter.");
            }

            return await OpenWithSorter(req, sorter, collectWd, ct);
        }

        var rsp = await rpc.Request<NewTunnelReq, NewTunnelRsp>(
            TunnelSubjects.NewTunnel, req, reqTimeout, ct);

        if (rsp.HasError)
        {
            Log.Debug("Tunnel request for subject '{Subject}' was rejected: {Error}", req.Subject, rsp.Error);

            throw new TunnelHandshakeException(
                $"Tunnel request for subject '{req.Subject}' was rejected: {rsp.Error}");
        }

        return await connector.Connect(rsp);
    }

    private async Task<RpcTunnel> OpenWithSorter(
        NewTunnelReq req,
        Func<IReadOnlyList<NewTunnelRsp>, NewTunnelRsp> sorter,
        TimeSpan window,
        CancellationToken token
    )
    {
        var replySubject = $"reply_{Random.Shared.NextUuid()}";

        var reqMsg = new RpcMessage(req.ToByteArray()) {
            ReplySubject = replySubject,
            Transport = rpc
        };

        var replies = new List<NewTunnelRsp>();

        using var sub = await rpc.Subscribe(replySubject, msg => {
            if (msg.TryDeserialize<NewTunnelRsp>() is not {} rsp)
                return Task.CompletedTask;

            if (rsp.HasError)
            {
                Log.Verbose("Tunnel request for subject '{Subject}' was rejected: {Error}", req.Subject, rsp.Error);
                return Task.CompletedTask;
            }

            lock (replies)
            {
                replies.Add(rsp);
            }
            return Task.CompletedTask;
        });

        await rpc.Publish(TunnelSubjects.NewTunnel, reqMsg);
        await Task.Delay(window, token);

        NewTunnelRsp[] snapshot;

        lock (replies)
        {
            snapshot = [.. replies];
        }

        if (snapshot.Length == 0)
            throw new TunnelHandshakeException($"No server responded to tunnel subject '{req.Subject}'.");

        var winner = sorter(Array.AsReadOnly(snapshot));
        return await connector.Connect(winner);
    }
}
