using Google.Protobuf;

namespace Starlight.Protobuf.Core;

/// <summary>
/// Stores wire fields encountered during deserialization that did not match any
/// known base field. Kept for inspection / live deobfuscation. The capture path
/// is only entered when an unmatched tag appears, so it is free on the hot path.
/// </summary>
public sealed class UnknownFieldSet
{
    private readonly List<UnknownField> _fields = [];

    public IReadOnlyList<UnknownField> Fields => _fields;

    public bool IsEmpty => _fields.Count == 0;

    public void Add(UnknownField field) => _fields.Add(field);

    /// <summary>
    /// Reads the value of the field identified by <paramref name="tag"/> off the
    /// wire and captures it as an <see cref="UnknownField"/>. Called from a
    /// generated deserializer's default switch branch, so <paramref name="tag"/>
    /// has already been consumed via <c>ReadTag</c>. The payload is stored as the
    /// raw value bytes (no tag) so it can be inspected or re-encoded later.
    /// </summary>
    public static UnknownField ReadFrom(uint tag, CodedInputStream input)
    {
        var number = WireFormat.GetTagFieldNumber(tag);
        var wireType = WireFormat.GetTagWireType(tag);

        switch (wireType)
        {
            case WireFormat.WireType.Varint:
                {
                    var value = input.ReadUInt64();
                    var buffer = new byte[CodedOutputStream.ComputeUInt64Size(value)];
                    using var output = new CodedOutputStream(buffer);
                    output.WriteUInt64(value);
                    output.Flush();
                    return new UnknownField(number, wireType, buffer);
                }
            case WireFormat.WireType.Fixed32:
                {
                    var buffer = new byte[4];
                    using var output = new CodedOutputStream(buffer);
                    output.WriteFixed32(input.ReadFixed32());
                    output.Flush();
                    return new UnknownField(number, wireType, buffer);
                }
            case WireFormat.WireType.Fixed64:
                {
                    var buffer = new byte[8];
                    using var output = new CodedOutputStream(buffer);
                    output.WriteFixed64(input.ReadFixed64());
                    output.Flush();
                    return new UnknownField(number, wireType, buffer);
                }
            case WireFormat.WireType.LengthDelimited:
                return new UnknownField(number, wireType, input.ReadBytes().ToByteArray());
            default:
                // Groups (start/end) are deprecated and unsupported; skip the
                // payload but still record that the field was present.
                input.SkipLastField();
                return new UnknownField(number, wireType, []);
        }
    }
}

/// <summary>A single unmatched wire field: its tag, wire type, and raw payload.</summary>
public readonly record struct UnknownField(int FieldNumber, WireFormat.WireType WireType, byte[] Data);
