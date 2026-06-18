using Google.Protobuf;
using Starlight.Protobuf.Core;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Protobuf.Registry;

/// <summary>
/// Per-version dispatcher. Each compiled protocol version emits one
/// <c>V[n]ProtocolRegistry</c> subclass whose bodies are hardcoded switches
/// over the shared POCO message types.
/// </summary>
public abstract class ProtocolRegistry
{
    /// <summary>Protocol version string (e.g. <c>"V66"</c>), derived from the version package.</summary>
    public abstract string Version { get; }

    /// <summary>
    /// CmdIds that may legally be a session's first packet for this version.
    /// Used to detect a connecting client's protocol version.
    /// </summary>
    public abstract IReadOnlySet<int> KnownFirst { get; }

    /// <summary>Resolves the CmdId for a message instance (hardcoded switch on POCO type).</summary>
    public abstract int GetCmdId(IMessage message);

    /// <summary>Constructs a new, empty POCO for the given CmdId.</summary>
    public abstract IMessage Create(int cmdId);

    /// <summary>Exact serialized size of <paramref name="message"/> for this version.</summary>
    public abstract int CalculateSize(IMessage message);

    /// <summary>Serializes <paramref name="message"/> into <paramref name="output"/> for this version.</summary>
    public abstract void Serialize(IMessage message, CodedOutputStream output);

    /// <summary>Deserializes wire data from <paramref name="input"/> into <paramref name="message"/> for this version.</summary>
    public abstract void Deserialize(IMessage message, CodedInputStream input);

    /// <summary>Serializes <paramref name="message"/> to a right-sized byte array.</summary>
    public byte[] Serialize(IMessage message)
    {
        var buffer = new byte[CalculateSize(message)];
        using var output = new CodedOutputStream(buffer);
        Serialize(message, output);
        output.CheckNoSpaceLeft();
        return buffer;
    }

    /// <summary>Inbound dispatch: constructs the POCO for <paramref name="cmdId"/> and fills it from <paramref name="input"/>.</summary>
    public IMessage Deserialize(int cmdId, CodedInputStream input)
    {
        var message = Create(cmdId);
        Deserialize(message, input);
        return message;
    }

    /// <summary>
    /// Field tables for this version's messages, used to register opt-in field-ID
    /// remaps (live deobfuscation). Empty for registries without compiled descriptors.
    /// </summary>
    public virtual IReadOnlyCollection<MessageDescriptor> Descriptors => [
    ];

    /// <summary>Field table for the message with the given CmdId, or <c>null</c>.</summary>
    public virtual MessageDescriptor? GetDescriptor(int cmdId) => null;

    /// <summary>Field table for the given POCO message type, or <c>null</c>.</summary>
    public virtual MessageDescriptor? GetDescriptor(Type messageType) => null;
}
