using System.Collections.Concurrent;
using Google.Protobuf;
using Starlight.Rpc;

namespace Starlight.Rpc;

/// <summary>
/// A fully in-memory, single process transport for 'remote' procedure calls.
/// </summary>
public sealed class DirectRpcTransport : RpcTransport
{
    private readonly ConcurrentDictionary<string, List<AsyncDataHandler>> _handlers = new();

    public override Task<IDisposable> Subscribe(string subject, AsyncDataHandler handler)
    {
        var handlers = _handlers.GetOrAdd(subject, _ => []);

        lock (handlers)
        {
            handlers.Add(handler);
        }
        
        return Task.FromResult<IDisposable>(new Subscription(handlers, handler));
    }

    public override async Task Publish(string subject, RpcMessage message)
    {
        if (_handlers.TryGetValue(subject, out var handlers))
        {
            List<AsyncDataHandler> snapshot;
            lock (handlers)
            {
                snapshot = [.. handlers];
            }
            foreach (var handler in snapshot)
            {
                await handler(message);
            }
        }
    }

    protected override RpcMessage Serialize(IMessage message) => new DirectRpcMessage(message);

    protected override bool HasResponders(string subject)
    {
        if (!_handlers.TryGetValue(subject, out var handlers)) return false;

        lock (handlers)
        {
            return handlers.Count > 0;
        }
    }

    public override Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class Subscription(List<AsyncDataHandler> handlers, AsyncDataHandler handler) : IDisposable
{
    public void Dispose()
    {
        lock (handlers)
        {
            handlers.Remove(handler);
        }
    }
}
