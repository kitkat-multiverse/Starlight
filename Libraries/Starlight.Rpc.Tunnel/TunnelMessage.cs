using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Rpc.Tunnel;

public abstract class TunnelMessage
{
    /// <summary>
    /// Ephemeral string frequency the requester listens on for the reply.
    /// Null for non-request publishes.
    /// </summary>
    public string? ReplyId { get; internal set; }

    /// <summary>
    /// The receiving end of the tunnel that delivered this message.
    /// Set by the transport's Deliver path; used by <see cref="Reply"/>.
    /// </summary>
    internal RpcTunnel? Tunnel { get; set; }

    public object? Metadata { get; protected set; }

    public abstract T? TryDecode<T>() where T : class, IMessage;

    public T Decode<T>() where T : class, IMessage
        => TryDecode<T>() ?? throw new TunnelDecodeException("Failed to decode tunnel message.");

    public abstract IMessage? TryDecode(Type type);

    public IMessage Decode(Type type)
        => TryDecode(type) ?? throw new TunnelDecodeException("Failed to decode tunnel message.");

    public async Task Reply(IMessage reply)
    {
        if (string.IsNullOrEmpty(ReplyId) || Tunnel is null)
            throw new InvalidOperationException("Message is not configured with reply values.");

        await Tunnel.Publish(ReplyId, reply);
    }
}
