using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Serilog;

namespace Starlight.Rpc;

public class RpcMessage(byte[] payload)
{
    private static readonly RpcMessage Empty = new([]);
    
    /// Allows converting byte payloads into messages.
    public static implicit operator RpcMessage(byte[] payload) => new(payload);
    
    public byte[] Payload => payload;

    public string? ReplySubject { protected get; set; }
    public RpcTransport? Transport { protected get; set; }
    
    public object? Metadata;

    /// <summary>
    /// Attempts to deserialize the message's payload as a Google Protobuf object.
    /// </summary>
    /// <typeparam name="T">The message to decode into.</typeparam>
    /// <returns>The parsed message, or null if it failed to parse.</returns>
    /// <exception cref="Exception">If the message doesn't have a parser.</exception>
    public virtual T? TryDeserialize<T>() where T : IMessage<T>
    {
        var parser = (MessageParser<T>?) typeof(T)
            .GetProperty("Parser")?
            .GetValue(null);

        try
        {
            return parser == null ?
                throw new Exception("Invalid protobuf message type.") :
                parser.ParseFrom(Payload);
        }
        catch (InvalidProtocolBufferException ex)
        {
            Log.Warning(ex, "Failed to deserialize RPC message.");
            return default;
        }
    }

    public T Deserialize<T>() where T : IMessage<T>
        => TryDeserialize<T>() ?? throw new NullReferenceException("Failed to deserialize message.");

    /// <summary>
    /// Sends a reply to the requester, if applicable.
    /// </summary>
    /// <param name="reply">The message to reply with. If null, an empty byte array is used.</param>
    public async Task Reply(RpcMessage? reply)
    {
        if (string.IsNullOrEmpty(ReplySubject) || Transport is null)
        {
            throw new NullReferenceException("Message is not configured with reply values.");
        }

        await Transport.Publish(ReplySubject, reply ?? Empty);
    }

    /// <inheritdoc cref="Reply"/>
    public async Task Reply<T>(T? reply) where T : IMessage
    {
        if (string.IsNullOrEmpty(ReplySubject) || Transport is null)
        {
            throw new NullReferenceException("Message is not configured with reply values.");
        }
        
        await Transport.Publish(ReplySubject, reply as IMessage ?? new Empty());
    }
}
