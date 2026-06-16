using Google.Protobuf;
using Starlight.Rpc;

namespace Starlight.Rpc;

public sealed class DirectRpcMessage : RpcMessage
{
    public DirectRpcMessage(IMessage message) : base([])
    {
        Metadata = message;
    }
    
    public override T? TryDeserialize<T>() where T : default
    {
        // For direct messages, we can hide the underlying struct
        // in the metadata.
        //
        // This allows us to skip the actual deserialization and
        // share the same memory.
        if (Metadata is T obj) return obj;

        return base.TryDeserialize<T>();
    }
}
