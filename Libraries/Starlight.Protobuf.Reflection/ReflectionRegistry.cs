using Google.Protobuf;
using Starlight.Protobuf.Core;
using Starlight.Protobuf.Registry;
using Starlight.Protobuf.Serialization;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Protobuf.Reflection;

/// <summary>
///     A <see cref="ProtocolRegistry" /> backed by <c>.proto</c> files parsed at runtime
///     rather than compiled code. Every message is a <see cref="DynamicMessage" /> and all
///     (de)serialization runs through the shared <see cref="ReflectiveEngine" />.
///     Intended as a <b>first-stop</b> resolver: query <see cref="Knows" /> for a CmdId and,
///     on a miss, fall through to the compiled version registries. Use cases are messages
///     not yet compiled in and live deobfuscation testing.
/// </summary>
public sealed class ReflectionRegistry : ProtocolRegistry
{
    private readonly MessageDescriptor[] _descriptors;
    private readonly ReflectionSchema _schema;

    public ReflectionRegistry(ReflectionSchema schema)
    {
        _schema = schema;
        _descriptors = schema.ByName.Values.ToArray();
    }

    public override string Version => _schema.Version;

    public override IReadOnlySet<int> KnownFirst => _schema.KnownFirst;

    public override IReadOnlyCollection<MessageDescriptor> Descriptors => _descriptors;

    public static ReflectionRegistry LoadFromDirectory(string directory, string? version = null) =>
        new(ReflectionSchema.LoadFromDirectory(directory, version));

    public static ReflectionRegistry Load(IReadOnlyDictionary<string, string> sources, string? version = null) =>
        new(ReflectionSchema.Load(sources, version));

    /// <summary>True if this registry has a descriptor for <paramref name="cmdId" /> (the first-stop hit test).</summary>
    public bool Knows(int cmdId) => _schema.CmdIdToName.ContainsKey(cmdId);

    /// <summary>Creates an empty <see cref="DynamicMessage" /> for a message by its proto name.</summary>
    public DynamicMessage CreateByName(string messageName) =>
        new(Descriptor(messageName));

    public override int GetCmdId(IMessage message)
    {
        var name = ((DynamicMessage)message).Descriptor.Name;
        return _schema.NameToCmdId.TryGetValue(name, out var id) ? id : 0;
    }

    public override IMessage Create(int cmdId)
    {
        if (!_schema.CmdIdToName.TryGetValue(cmdId, out var name))
            throw new ArgumentOutOfRangeException(nameof(cmdId), cmdId, $"Unknown CmdId for reflection registry '{Version}'.");

        return new DynamicMessage(Descriptor(name));
    }

    public override int CalculateSize(IMessage message)
    {
        var dynamic = (DynamicMessage)message;
        return ReflectiveEngine.CalculateSize(dynamic.Descriptor, dynamic);
    }

    public override void Serialize(IMessage message, CodedOutputStream output)
    {
        var dynamic = (DynamicMessage)message;
        ReflectiveEngine.Serialize(dynamic.Descriptor, dynamic, output);
    }

    public override void Deserialize(IMessage message, CodedInputStream input)
    {
        var dynamic = (DynamicMessage)message;
        ReflectiveEngine.Deserialize(dynamic.Descriptor, dynamic, input);
    }

    public override MessageDescriptor? GetDescriptor(int cmdId) =>
        _schema.CmdIdToName.TryGetValue(cmdId, out var name) ? _schema.ByName[name] : null;

    /// <summary>Dynamic messages carry no CLR type, so type-keyed lookup never matches.</summary>
    public override MessageDescriptor? GetDescriptor(Type messageType) => null;

    private MessageDescriptor Descriptor(string name) =>
        _schema.ByName.TryGetValue(name, out var d) ?
            d :
            throw new ArgumentException($"Unknown message '{name}' in reflection registry '{Version}'.", nameof(name));
}
