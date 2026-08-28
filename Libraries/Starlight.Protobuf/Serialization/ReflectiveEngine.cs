using System.Collections;
using Google.Protobuf;
using Starlight.Protobuf.Core;
using WireType = Google.Protobuf.WireFormat.WireType;
using IMessage = Starlight.Protobuf.Core.IMessage;
using UnknownFieldSet = Starlight.Protobuf.Core.UnknownFieldSet;

namespace Starlight.Protobuf.Serialization;

/// <summary>
/// Descriptor-driven (de)serialization shared by the opt-in remap slow path
/// (compiled POCOs) and the reflection registry (dynamic messages). Off the hot
/// path: a message only routes here when <see cref="MessageDescriptor.HasRemaps"/>
/// is set, or when it has no compiled serializer at all. Field encoding matches
/// the generated fast path byte-for-byte when no remap is active.
/// </summary>
public static class ReflectiveEngine
{
    // ---- size ---------------------------------------------------------------

    public static int CalculateSize(MessageDescriptor desc, object msg)
    {
        var size = 0;

        foreach (var f in desc.Fields)
        {
            switch (f.Rule)
            {
                case FieldRule.Map: size += MapSize(desc, msg, f); break;
                case FieldRule.Repeated: size += RepeatedSize(desc, msg, f); break;
                default: size += SingleSize(desc, msg, f); break;
            }
        }

        return size;
    }

    private static int SingleSize(MessageDescriptor desc, object msg, FieldDescriptor f)
    {
        if (!TryGetSingle(desc, msg, f, out var value)) return 0;

        var tag = CodedOutputStream.ComputeTagSize(f.Number);

        if (f.Kind == ProtoKind.Message)
        {
            var s = CalculateSize(f.MessageRef!(), value!);
            return tag + CodedOutputStream.ComputeLengthSize(s) + s;
        }

        return tag + ValueSize(f.Kind, EncodeWire(f, value!));
    }

    private static int RepeatedSize(MessageDescriptor desc, object msg, FieldDescriptor f)
    {
        var list = desc.GetList(msg, f);
        if (list.Count == 0) return 0;

        var tag = CodedOutputStream.ComputeTagSize(f.Number);

        if (f.Kind == ProtoKind.Message)
        {
            var total = 0;

            foreach (var v in list)
            {
                var s = CalculateSize(f.MessageRef!(), v!);
                total += tag + CodedOutputStream.ComputeLengthSize(s) + s;
            }

            return total;
        }

        if (f.Kind is ProtoKind.String or ProtoKind.Bytes)
        {
            var total = 0;

            foreach (var v in list)
            {
                total += tag + ValueSize(f.Kind, v!);
            }
            return total;
        }

        // packed scalar/enum
        var d = 0;

        foreach (var v in list)
        {
            d += ValueSize(f.Kind, Norm(f.Kind, v));
        }
        return tag + CodedOutputStream.ComputeLengthSize(d) + d;
    }

    private static int MapSize(MessageDescriptor desc, object msg, FieldDescriptor f)
    {
        var map = desc.GetMap(msg, f);
        if (map.Count == 0) return 0;

        var tag = CodedOutputStream.ComputeTagSize(f.Number);
        var keyTag = CodedOutputStream.ComputeTagSize(1);
        var valTag = CodedOutputStream.ComputeTagSize(2);

        var total = 0;

        foreach (DictionaryEntry e in map)
        {
            var es = keyTag + ValueSize(f.KeyKind, Norm(f.KeyKind, e.Key)) + valTag + MapValueSize(f, e.Value!);
            total += tag + CodedOutputStream.ComputeLengthSize(es) + es;
        }

        return total;
    }

    private static int MapValueSize(FieldDescriptor f, object value)
    {
        if (f.Kind != ProtoKind.Message) return ValueSize(f.Kind, Norm(f.Kind, value));

        var s = CalculateSize(f.MessageRef!(), value);
        return CodedOutputStream.ComputeLengthSize(s) + s;
    }

    // ---- serialize ----------------------------------------------------------

    public static void Serialize(MessageDescriptor desc, object msg, CodedOutputStream output)
    {
        foreach (var f in desc.Fields)
        {
            switch (f.Rule)
            {
                case FieldRule.Map: WriteMap(desc, msg, f, output); break;
                case FieldRule.Repeated: WriteRepeated(desc, msg, f, output); break;
                default: WriteSingle(desc, msg, f, output); break;
            }
        }
    }

    private static void WriteSingle(MessageDescriptor desc, object msg, FieldDescriptor f, CodedOutputStream output)
    {
        if (!TryGetSingle(desc, msg, f, out var value)) return;

        if (f.Kind == ProtoKind.Message)
        {
            var nested = f.MessageRef!();
            output.WriteTag(f.Number, WireType.LengthDelimited);
            output.WriteLength(CalculateSize(nested, value!));
            Serialize(nested, value!, output);
            return;
        }

        output.WriteTag(f.Number, WireTypeOf(f.Kind));
        WriteValue(output, f.Kind, EncodeWire(f, value!));
    }

    private static void WriteRepeated(MessageDescriptor desc, object msg, FieldDescriptor f, CodedOutputStream output)
    {
        var list = desc.GetList(msg, f);
        if (list.Count == 0) return;

        if (f.Kind == ProtoKind.Message)
        {
            var nested = f.MessageRef!();

            foreach (var v in list)
            {
                output.WriteTag(f.Number, WireType.LengthDelimited);
                output.WriteLength(CalculateSize(nested, v!));
                Serialize(nested, v!, output);
            }

            return;
        }

        if (f.Kind is ProtoKind.String or ProtoKind.Bytes)
        {
            foreach (var v in list)
            {
                output.WriteTag(f.Number, WireType.LengthDelimited);
                WriteValue(output, f.Kind, v!);
            }

            return;
        }

        // packed scalar/enum
        var d = 0;

        foreach (var v in list)
        {
            d += ValueSize(f.Kind, Norm(f.Kind, v));
        }
        output.WriteTag(f.Number, WireType.LengthDelimited);
        output.WriteLength(d);

        foreach (var v in list)
        {
            WriteValue(output, f.Kind, Norm(f.Kind, v));
        }
    }

    private static void WriteMap(MessageDescriptor desc, object msg, FieldDescriptor f, CodedOutputStream output)
    {
        var map = desc.GetMap(msg, f);
        if (map.Count == 0) return;

        var keyTag = CodedOutputStream.ComputeTagSize(1);
        var valTag = CodedOutputStream.ComputeTagSize(2);

        foreach (DictionaryEntry e in map)
        {
            var key = Norm(f.KeyKind, e.Key);
            var es = keyTag + ValueSize(f.KeyKind, key) + valTag + MapValueSize(f, e.Value!);
            output.WriteTag(f.Number, WireType.LengthDelimited);
            output.WriteLength(es);
            output.WriteTag(fieldNumber: 1, WireTypeOf(f.KeyKind));
            WriteValue(output, f.KeyKind, key);

            if (f.Kind == ProtoKind.Message)
            {
                var nested = f.MessageRef!();
                output.WriteTag(fieldNumber: 2, WireType.LengthDelimited);
                output.WriteLength(CalculateSize(nested, e.Value!));
                Serialize(nested, e.Value!, output);
            } else
            {
                output.WriteTag(fieldNumber: 2, WireTypeOf(f.Kind));
                WriteValue(output, f.Kind, Norm(f.Kind, e.Value!));
            }
        }
    }

    // ---- deserialize --------------------------------------------------------

    public static void Deserialize(MessageDescriptor desc, object msg, CodedInputStream input)
    {
        uint tag;

        while ((tag = input.ReadTag()) != 0)
        {
            var number = WireFormat.GetTagFieldNumber(tag);
            var wire = WireFormat.GetTagWireType(tag);
            var f = desc.FindByNumber(number);

            if (f is null)
            {
                var m = (IMessage)msg;
                (m.UnknownFields ??= new UnknownFieldSet()).Add(UnknownFieldSet.ReadFrom(tag, input));
                continue;
            }

            ReadInto(desc, msg, f, wire, input);
        }
    }

    private static void ReadInto(MessageDescriptor desc, object msg, FieldDescriptor f, WireType wire, CodedInputStream input)
    {
        switch (f.Rule)
        {
            case FieldRule.Map:
                ReadMapEntry(desc, msg, f, input);
                return;

            case FieldRule.Repeated when f.Kind == ProtoKind.Message: {
                var nested = f.MessageRef!();
                var sub = nested.Factory!();
                Deserialize(nested, sub, input.ReadBytes().CreateCodedInput());
                desc.GetList(msg, f).Add(sub);
                return;
            }

            case FieldRule.Repeated when f.Kind is ProtoKind.String or ProtoKind.Bytes:
                desc.AddElement(desc.GetList(msg, f), f, ReadValue(input, f.Kind));
                return;

            case FieldRule.Repeated: {
                var list = desc.GetList(msg, f);

                if (wire == WireType.LengthDelimited)
                {
                    var ci = input.ReadBytes().CreateCodedInput();

                    while (!ci.IsAtEnd)
                        desc.AddElement(list, f, ReadValue(ci, f.Kind));
                } else
                {
                    desc.AddElement(list, f, ReadValue(input, f.Kind));
                }

                return;
            }

            default:
                if (f.Kind == ProtoKind.Message)
                {
                    var nested = f.MessageRef!();
                    var sub = nested.Factory!();
                    Deserialize(nested, sub, input.ReadBytes().CreateCodedInput());

                    if (f.InOneof) desc.SetOneof(msg, f, sub);
                    else desc.SetValue(msg, f, sub);
                } else
                {
                    var value = DecodeWire(f, ReadValue(input, f.Kind));

                    if (f.InOneof) desc.SetOneof(msg, f, value);
                    else desc.SetValue(msg, f, value);
                }

                return;
        }
    }

    private static void ReadMapEntry(MessageDescriptor desc, object msg, FieldDescriptor f, CodedInputStream input)
    {
        var ci = input.ReadBytes().CreateCodedInput();
        var key = DefaultOf(f.KeyKind);
        var value = f.Kind == ProtoKind.Message ? null : DefaultOf(f.Kind);

        uint t;

        while ((t = ci.ReadTag()) != 0)
        {
            switch (WireFormat.GetTagFieldNumber(t))
            {
                case 1:
                    key = ReadValue(ci, f.KeyKind);
                    break;
                case 2:
                    if (f.Kind == ProtoKind.Message)
                    {
                        var nested = f.MessageRef!();
                        value = nested.Factory!();
                        Deserialize(nested, value, ci.ReadBytes().CreateCodedInput());
                    } else
                    {
                        value = ReadValue(ci, f.Kind);
                    }

                    break;
                default:
                    ci.SkipLastField();
                    break;
            }
        }

        if (f.Kind == ProtoKind.Message && value is null)
        {
            var nested = f.MessageRef!();

            value = nested.Factory?.Invoke()
                    ?? throw new InvalidOperationException($"Map field '{f.Name}' message type '{nested.Name}' has no factory.");
        }

        desc.PutEntry(desc.GetMap(msg, f), key, value);
    }

    // ---- presence -----------------------------------------------------------

    /// <summary>Resolves a single field's value and whether it should be written.</summary>
    private static bool TryGetSingle(MessageDescriptor desc, object msg, FieldDescriptor f, out object? value)
    {
        if (f.InOneof)
        {
            if (!desc.OneofActive(msg, f))
            {
                value = null;
                return false;
            }
            value = desc.GetOneof(msg, f);
            return true; // oneof writes even a default value
        }

        value = desc.GetValue(msg, f);
        if (f.Rule == FieldRule.Optional) return value is not null; // explicit presence
        if (f.Kind == ProtoKind.Message) return value is not null; // presence via null

        return !IsDefault(f.Kind, value!); // implicit proto3 omission
    }

    private static bool IsDefault(ProtoKind kind, object value) => kind switch {
        ProtoKind.String => ((string)value).Length == 0,
        ProtoKind.Bytes => ((ByteString)value).Length == 0,
        ProtoKind.Bool => !(bool)value,
        ProtoKind.Float => (float)value == 0f,
        ProtoKind.Double => (double)value == 0d,
        ProtoKind.Int64 or ProtoKind.SInt64 or ProtoKind.SFixed64 => (long)value == 0L,
        ProtoKind.UInt32 or ProtoKind.Fixed32 => (uint)value == 0u,
        ProtoKind.UInt64 or ProtoKind.Fixed64 => (ulong)value == 0UL,
        _ => Convert.ToInt64(value) == 0L // int32/sint32/sfixed32/enum
    };

    // ---- wire primitives ----------------------------------------------------

    private static WireType WireTypeOf(ProtoKind kind) => kind switch {
        ProtoKind.Double or ProtoKind.Fixed64 or ProtoKind.SFixed64 => WireType.Fixed64,
        ProtoKind.Float or ProtoKind.Fixed32 or ProtoKind.SFixed32 => WireType.Fixed32,
        ProtoKind.String or ProtoKind.Bytes or ProtoKind.Message => WireType.LengthDelimited,
        _ => WireType.Varint
    };

    private static int ValueSize(ProtoKind kind, object v) => kind switch {
        ProtoKind.Double => CodedOutputStream.ComputeDoubleSize((double)v),
        ProtoKind.Float => CodedOutputStream.ComputeFloatSize((float)v),
        ProtoKind.Int32 => CodedOutputStream.ComputeInt32Size((int)v),
        ProtoKind.Int64 => CodedOutputStream.ComputeInt64Size((long)v),
        ProtoKind.UInt32 => CodedOutputStream.ComputeUInt32Size((uint)v),
        ProtoKind.UInt64 => CodedOutputStream.ComputeUInt64Size((ulong)v),
        ProtoKind.SInt32 => CodedOutputStream.ComputeSInt32Size((int)v),
        ProtoKind.SInt64 => CodedOutputStream.ComputeSInt64Size((long)v),
        ProtoKind.Fixed32 => CodedOutputStream.ComputeFixed32Size((uint)v),
        ProtoKind.Fixed64 => CodedOutputStream.ComputeFixed64Size((ulong)v),
        ProtoKind.SFixed32 => CodedOutputStream.ComputeSFixed32Size((int)v),
        ProtoKind.SFixed64 => CodedOutputStream.ComputeSFixed64Size((long)v),
        ProtoKind.Bool => CodedOutputStream.ComputeBoolSize((bool)v),
        ProtoKind.String => CodedOutputStream.ComputeStringSize((string)v),
        ProtoKind.Bytes => CodedOutputStream.ComputeBytesSize((ByteString)v),
        ProtoKind.Enum => CodedOutputStream.ComputeEnumSize((int)v),
        _ => throw new InvalidOperationException($"Not a scalar kind: {kind}")
    };

    private static void WriteValue(CodedOutputStream o, ProtoKind kind, object v)
    {
        switch (kind)
        {
            case ProtoKind.Double: o.WriteDouble((double)v); break;
            case ProtoKind.Float: o.WriteFloat((float)v); break;
            case ProtoKind.Int32: o.WriteInt32((int)v); break;
            case ProtoKind.Int64: o.WriteInt64((long)v); break;
            case ProtoKind.UInt32: o.WriteUInt32((uint)v); break;
            case ProtoKind.UInt64: o.WriteUInt64((ulong)v); break;
            case ProtoKind.SInt32: o.WriteSInt32((int)v); break;
            case ProtoKind.SInt64: o.WriteSInt64((long)v); break;
            case ProtoKind.Fixed32: o.WriteFixed32((uint)v); break;
            case ProtoKind.Fixed64: o.WriteFixed64((ulong)v); break;
            case ProtoKind.SFixed32: o.WriteSFixed32((int)v); break;
            case ProtoKind.SFixed64: o.WriteSFixed64((long)v); break;
            case ProtoKind.Bool: o.WriteBool((bool)v); break;
            case ProtoKind.String: o.WriteString((string)v); break;
            case ProtoKind.Bytes: o.WriteBytes((ByteString)v); break;
            case ProtoKind.Enum: o.WriteEnum((int)v); break;
            default: throw new InvalidOperationException($"Not a scalar kind: {kind}");
        }
    }

    private static object ReadValue(CodedInputStream i, ProtoKind kind) => kind switch {
        ProtoKind.Double => i.ReadDouble(),
        ProtoKind.Float => i.ReadFloat(),
        ProtoKind.Int32 => i.ReadInt32(),
        ProtoKind.Int64 => i.ReadInt64(),
        ProtoKind.UInt32 => i.ReadUInt32(),
        ProtoKind.UInt64 => i.ReadUInt64(),
        ProtoKind.SInt32 => i.ReadSInt32(),
        ProtoKind.SInt64 => i.ReadSInt64(),
        ProtoKind.Fixed32 => i.ReadFixed32(),
        ProtoKind.Fixed64 => i.ReadFixed64(),
        ProtoKind.SFixed32 => i.ReadSFixed32(),
        ProtoKind.SFixed64 => i.ReadSFixed64(),
        ProtoKind.Bool => i.ReadBool(),
        ProtoKind.String => i.ReadString(),
        ProtoKind.Bytes => i.ReadBytes(),
        ProtoKind.Enum => i.ReadEnum(),
        _ => throw new InvalidOperationException($"Not a scalar kind: {kind}")
    };

    private static object DefaultOf(ProtoKind kind) => kind switch {
        ProtoKind.String => "",
        ProtoKind.Bytes => ByteString.Empty,
        ProtoKind.Bool => false,
        ProtoKind.Float => 0f,
        ProtoKind.Double => 0d,
        ProtoKind.Int64 or ProtoKind.SInt64 or ProtoKind.SFixed64 => 0L,
        ProtoKind.UInt32 or ProtoKind.Fixed32 => 0u,
        ProtoKind.UInt64 or ProtoKind.Fixed64 => 0UL,
        _ => 0 // int32/sint32/sfixed32/enum
    };

    /// <summary>Normalizes a CLR enum element to its int wire form; passes others through.</summary>
    private static object Norm(ProtoKind kind, object? v) =>
        kind == ProtoKind.Enum ? Convert.ToInt32(v) : v!;

    // ---- field transforms (single scalar integer obfuscation) ---------------

    private static object EncodeWire(FieldDescriptor f, object value) =>
        f.Transform is null ? value : FromLong(f.Kind, f.Transform.Encode(ToLong(f.Kind, value)));

    private static object DecodeWire(FieldDescriptor f, object value) =>
        f.Transform is null ? value : FromLong(f.Kind, f.Transform.Decode(ToLong(f.Kind, value)));

    private static long ToLong(ProtoKind kind, object v) => kind switch {
        ProtoKind.Int32 or ProtoKind.SInt32 or ProtoKind.SFixed32 => (int)v,
        ProtoKind.Int64 or ProtoKind.SInt64 or ProtoKind.SFixed64 => (long)v,
        ProtoKind.UInt32 or ProtoKind.Fixed32 => (uint)v,
        ProtoKind.UInt64 or ProtoKind.Fixed64 => unchecked((long)(ulong)v),
        _ => throw new InvalidOperationException($"Transform not supported for kind: {kind}")
    };

    private static object FromLong(ProtoKind kind, long v) => kind switch {
        ProtoKind.Int32 or ProtoKind.SInt32 or ProtoKind.SFixed32 => unchecked((int)v),
        ProtoKind.Int64 or ProtoKind.SInt64 or ProtoKind.SFixed64 => v,
        ProtoKind.UInt32 or ProtoKind.Fixed32 => unchecked((uint)v),
        ProtoKind.UInt64 or ProtoKind.Fixed64 => unchecked((ulong)v),
        _ => throw new InvalidOperationException($"Transform not supported for kind: {kind}")
    };
}
