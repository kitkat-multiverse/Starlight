using Starlight.Common;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Rpc.Tunnel;

public delegate Task AsyncTunnelHandler(TunnelMessage message);
public delegate Task AsyncTunnelHandler<in T>(T message, TunnelMessage raw) where T : class, IMessage;

/// <summary>
/// One end of a direct, point-to-point RPC tunnel.
/// <br/>
/// Publish sends to the peer; Subscribe registers handlers on this end.
/// Frequencies are either numeric (packet/cmd IDs) or string (control flow);
/// the two namespaces are fully isolated.
/// </summary>
public abstract class RpcTunnel : IDisposable
{
    private readonly CancellationTokenSource _closed = new();
    private int _closedFlag;

    public CancellationToken Closed => _closed.Token;
    public bool IsClosed => _closedFlag != 0;

    public event Action? OnClosed;

    // --- transport surface (implemented by concrete transports) ---

    /// <summary>Wraps <paramref name="message"/> in a transport-appropriate payload container.</summary>
    protected abstract TunnelMessage Serialize(IMessage message);

    /// <summary>Stamps the delivering (receiving) end onto <paramref name="message"/> so <see cref="TunnelMessage.Reply"/> routes back to the original sender.</summary>
    protected static void BindReceiver(TunnelMessage message, RpcTunnel receiver) => message.Tunnel = receiver;

    public abstract IDisposable Subscribe(int id, AsyncTunnelHandler handler);
    public abstract IDisposable Subscribe(string id, AsyncTunnelHandler handler);

    /// <summary>Low-level publish. Delivers a pre-built <see cref="TunnelMessage"/> to the peer.</summary>
    public abstract Task Publish(int id, TunnelMessage message);

    /// <inheritdoc cref="Publish(int,TunnelMessage)"/>
    public abstract Task Publish(string id, TunnelMessage message);

    // --- ergonomic overloads ---

    public Task Publish(int id, IMessage message) => Publish(id, Serialize(message));
    public Task Publish(string id, IMessage message) => Publish(id, Serialize(message));

    public IDisposable Subscribe<T>(int id, AsyncTunnelHandler<T> handler) where T : class, IMessage
        => Subscribe(id, Wrap(handler));

    public IDisposable Subscribe<T>(string id, AsyncTunnelHandler<T> handler) where T : class, IMessage
        => Subscribe(id, Wrap(handler));

    private static AsyncTunnelHandler Wrap<T>(AsyncTunnelHandler<T> handler) where T : class, IMessage
        => async msg => {
            if (msg.TryDecode<T>() is {} t) await handler(t, msg);
        };

    // --- request/reply ---

    /// <summary>
    /// Publishes <paramref name="request"/> on the numeric <paramref name="id"/>,
    /// then awaits a single reply on an ephemeral string id.
    /// </summary>
    public Task<TRsp> Request<TRsp>(int id, IMessage request, TimeSpan? timeout = null, CancellationToken ct = default)
        where TRsp : class, IMessage
        => RequestCore<TRsp>(request, m => Publish(id, m), id.ToString(), timeout, ct);

    /// <summary>
    /// Publishes <paramref name="request"/> on the string <paramref name="id"/>,
    /// then awaits a single reply on an ephemeral string id.
    /// </summary>
    public Task<TRsp> Request<TRsp>(string id, IMessage request, TimeSpan? timeout = null, CancellationToken ct = default)
        where TRsp : class, IMessage
        => RequestCore<TRsp>(request, m => Publish(id, m), id, timeout, ct);

    private async Task<TRsp> RequestCore<TRsp>(
        IMessage request,
        Func<TunnelMessage, Task> publish,
        string label,
        TimeSpan? timeout,
        CancellationToken ct = default
    )
        where TRsp : class, IMessage
    {
        ThrowIfClosed();
        timeout ??= TimeSpan.FromSeconds(5);

        var replyFreq = $"reply_{Random.Shared.NextUuid()}";
        var message = Serialize(request);
        message.ReplyId = replyFreq;

        var tcs = new TaskCompletionSource<TunnelMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var sub = Subscribe(replyFreq, msg => {
            tcs.TrySetResult(msg);
            return Task.CompletedTask;
        });

        await publish(message);

        TunnelMessage reply;

        try
        {
            await using var reg = Closed.Register(() => tcs.TrySetCanceled(ct));
            reply = await tcs.Task.WaitAsync(timeout.Value, ct);
        }
        catch (OperationCanceledException) when (IsClosed)
        {
            throw new TunnelClosedException();
        }
        catch (TimeoutException)
        {
            throw new TunnelRequestTimeoutException(label, timeout.Value);
        }

        return reply.Decode<TRsp>();
    }

    // --- closure ---

    public virtual void Close()
    {
        if (Interlocked.Exchange(ref _closedFlag, value: 1) != 0) return;

        _closed.Cancel();

        try
        {
            OnClosed?.Invoke();
        }
        finally
        {
            NotifyPeerClosed();
            OnSelfClosed();
        }
    }

    protected virtual void OnSelfClosed()
    {
    }

    /// <summary>Called by the peer's <see cref="Close"/>; cancels without re-notifying.</summary>
    protected void MarkClosedFromPeer()
    {
        if (Interlocked.Exchange(ref _closedFlag, value: 1) != 0) return;

        _closed.Cancel();

        try
        {
            OnClosed?.Invoke();
        }
        finally
        {
            OnSelfClosed();
        }
    }

    protected abstract void NotifyPeerClosed();

    protected void ThrowIfClosed()
    {
        if (IsClosed) throw new TunnelClosedException();
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;

        Close();
        _closed.Dispose();
    }
}
