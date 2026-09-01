using Google.Protobuf;
using Starlight.Protobuf.Core;
using Starlight.Protobuf.Serialization;
using System.Collections;
using UnknownFieldSet = Starlight.Protobuf.Core.UnknownFieldSet;

namespace Starlight.Protobuf.Reflection;

/// <summary>
/// A schema-less protobuf message: a property bag keyed by canonical field name,
/// driven entirely by its <see cref="MessageDescriptor"/>. Produced by the
/// <see cref="ReflectionRegistry"/> for messages loaded from <c>.proto</c> text at
/// runtime (no compiled POCO). The shared <see cref="ReflectiveEngine"/> reads and
/// writes it through <see cref="IDynamicAccessor"/>.
/// </summary>
public sealed class DynamicMessage : IDynamicMessage
{
    private readonly Dictionary<string, object?> _values = new();
    private readonly Dictionary<string, IList> _lists = new();
    private readonly Dictionary<string, IDictionary> _maps = new();
    private readonly Dictionary<string, (int Case, object? Value)> _oneofs = new();

    public DynamicMessage(MessageDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public MessageDescriptor Descriptor { get; }

    public UnknownFieldSet? UnknownFields { get; set; }

    public object? Get(string field)
    {
        if (_values.TryGetValue(field, out var v)) return v;

        // Unset: optional and message fields are absent (null); implicit scalars
        // report their proto3 zero so the engine's presence check sees a default.
        var f = Descriptor.Find(field);

        if (f is null || f.Rule == FieldRule.Optional || f.Kind == ProtoKind.Message)
            return null;

        return ScalarDefault(f.Kind);
    }

    public void Set(string field, object? value) => _values[field] = value;

    public IList GetList(string field) =>
        _lists.TryGetValue(field, out var l) ? l : _lists[field] = new List<object?>();

    public IDictionary GetMap(string field) =>
        _maps.TryGetValue(field, out var m) ? m : _maps[field] = new Dictionary<object, object?>();

    public int ActiveOneof(string oneofName) =>
        _oneofs.TryGetValue(oneofName, out var o) ? o.Case : 0;

    public object? GetOneof(string oneofName) =>
        _oneofs.TryGetValue(oneofName, out var o) ? o.Value : null;

    public void SetOneof(string oneofName, int caseNumber, object? value) =>
        _oneofs[oneofName] = (caseNumber, value);

    private static object ScalarDefault(ProtoKind kind) => kind switch {
        ProtoKind.String => "",
        ProtoKind.Bytes => ByteString.Empty,
        ProtoKind.Bool => false,
        ProtoKind.Float => 0f,
        ProtoKind.Double => 0d,
        ProtoKind.Int64 or ProtoKind.SInt64 or ProtoKind.SFixed64 => 0L,
        ProtoKind.UInt32 or ProtoKind.Fixed32 => 0u,
        ProtoKind.UInt64 or ProtoKind.Fixed64 => 0UL,
        _ => 0 // int32 / sint32 / sfixed32 / enum
    };
}
