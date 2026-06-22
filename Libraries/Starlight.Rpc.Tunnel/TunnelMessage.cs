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

    /// <summary>
    /// Optional publisher-supplied header bytes carried alongside the payload, opaque to the
    /// tunnel. The gate uses this to forward a packet's <c>PacketHead</c> metadata to the game
    /// server, which reconstructs the header lazily only when a handler actually needs it.
    /// </summary>
    public byte[]? Header { get; internal set; }

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
