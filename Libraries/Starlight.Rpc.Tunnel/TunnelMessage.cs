using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Rpc.Tunnel;

public class TunnelMessage
{
    /// <summary>
    /// Ephemeral string frequency the requester listens on for the reply.
    /// Null for non-request publishes.
    /// </summary>
    public string? ReplyFrequency { get; set; }

    /// <summary>
    /// The receiving end of the tunnel that delivered this message.
    /// Set by the transport's Deliver path; used by <see cref="Reply"/>.
    /// </summary>
    public RpcTunnel? Tunnel { get; set; }

    /// <summary>Zero-copy stash slot for in-memory transports.</summary>
    public object? Metadata;

    public virtual T? TryDecode<T>() where T : class, IMessage
        => throw new NotSupportedException("Binary decode requires a registry-aware transport.");

    public T Decode<T>() where T : class, IMessage
        => TryDecode<T>() ?? throw new NullReferenceException("Failed to decode tunnel message.");

    public virtual IMessage? TryDecode(Type type)
        => throw new NotSupportedException("Binary decode requires a registry-aware transport.");

    public IMessage Decode(Type type)
        => TryDecode(type) ?? throw new NullReferenceException("Failed to decode tunnel message.");

    public async Task Reply(IMessage reply)
    {
        if (string.IsNullOrEmpty(ReplyFrequency) || Tunnel is null)
            throw new InvalidOperationException("Message is not configured with reply values.");
        await Tunnel.Publish(ReplyFrequency, reply);
    }
}
