using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf.Reflection;
using FType = Google.Protobuf.Reflection.FieldDescriptorProto.Type;
using Label = Google.Protobuf.Reflection.FieldDescriptorProto.Label;

namespace Starlight.Protobuf.Compiler;

internal static partial class CodeEmitter
{
    // ---- wire info ----------------------------------------------------------

    private sealed class Wire
    {
        public string CsType = "";
        public int WireType;
        public string Write = "";
        public string Read = "";
        public string Compute = "";
        public bool IsEnum;
    }

    private static Wire Scalar(FType type, string enumCsType)
    {
        switch (type)
        {
            case FType.TypeDouble:
                return new Wire { CsType = "double", WireType = 1, Write = "WriteDouble", Read = "ReadDouble", Compute = "ComputeDoubleSize" };
            case FType.TypeFloat:
                return new Wire { CsType = "float", WireType = 5, Write = "WriteFloat", Read = "ReadFloat", Compute = "ComputeFloatSize" };
            case FType.TypeInt64:
                return new Wire { CsType = "long", WireType = 0, Write = "WriteInt64", Read = "ReadInt64", Compute = "ComputeInt64Size" };
            case FType.TypeUint64:
                return new Wire { CsType = "ulong", WireType = 0, Write = "WriteUInt64", Read = "ReadUInt64", Compute = "ComputeUInt64Size" };
            case FType.TypeInt32:
                return new Wire { CsType = "int", WireType = 0, Write = "WriteInt32", Read = "ReadInt32", Compute = "ComputeInt32Size" };
            case FType.TypeFixed64:
                return new Wire
                    { CsType = "ulong", WireType = 1, Write = "WriteFixed64", Read = "ReadFixed64", Compute = "ComputeFixed64Size" };
            case FType.TypeFixed32:
                return new Wire
                    { CsType = "uint", WireType = 5, Write = "WriteFixed32", Read = "ReadFixed32", Compute = "ComputeFixed32Size" };
            case FType.TypeBool:
                return new Wire { CsType = "bool", WireType = 0, Write = "WriteBool", Read = "ReadBool", Compute = "ComputeBoolSize" };
            case FType.TypeString:
                return new Wire { CsType = "string", WireType = 2, Write = "WriteString", Read = "ReadString", Compute = "ComputeStringSize" };
            case FType.TypeBytes:
                return new Wire {
                    CsType = "global::Google.Protobuf.ByteString", WireType = 2, Write = "WriteBytes", Read = "ReadBytes",
                    Compute = "ComputeBytesSize"
                };
            case FType.TypeUint32:
                return new Wire { CsType = "uint", WireType = 0, Write = "WriteUInt32", Read = "ReadUInt32", Compute = "ComputeUInt32Size" };
            case FType.TypeSfixed32:
                return new Wire
                    { CsType = "int", WireType = 5, Write = "WriteSFixed32", Read = "ReadSFixed32", Compute = "ComputeSFixed32Size" };
            case FType.TypeSfixed64:
                return new Wire
                    { CsType = "long", WireType = 1, Write = "WriteSFixed64", Read = "ReadSFixed64", Compute = "ComputeSFixed64Size" };
            case FType.TypeSint32:
                return new Wire { CsType = "int", WireType = 0, Write = "WriteSInt32", Read = "ReadSInt32", Compute = "ComputeSInt32Size" };
            case FType.TypeSint64:
                return new Wire { CsType = "long", WireType = 0, Write = "WriteSInt64", Read = "ReadSInt64", Compute = "ComputeSInt64Size" };
            case FType.TypeEnum:
                return new Wire
                    { CsType = enumCsType, WireType = 0, Write = "WriteEnum", Read = "ReadEnum", Compute = "ComputeEnumSize", IsEnum = true };
            default: throw new InvalidOperationException($"Unsupported scalar proto type: {type}");
        }
    }

    private static byte[] TagBytes(int number, int wireType)
    {
        var tag = (uint)number << 3 | (uint)wireType;
        var bytes = new List<byte>();

        do
        {
            var b = (byte)(tag & 0x7F);
            tag >>= 7;
            if (tag != 0) b |= 0x80;
            bytes.Add(b);
        } while (tag != 0);

        return bytes.ToArray();
    }

    private static uint TagValue(int number, int wireType) => (uint)number << 3 | (uint)wireType;

    private static string RawTag(int number, int wireType) =>
        $"output.WriteRawTag({string.Join(", ", TagBytes(number, wireType).Select(b => "0x" + b.ToString("X2")))})";

    private static int TagLen(int number, int wireType) => TagBytes(number, wireType).Length;

    // expression helpers (acc = the value expression being written/sized/read)
    private static string WriteCall(Wire w, string acc) =>
        $"output.{w.Write}({(w.IsEnum ? $"(int) {acc}" : acc)})";

    private static string SizeCall(Wire w, string acc) =>
        $"global::Google.Protobuf.CodedOutputStream.{w.Compute}({(w.IsEnum ? $"(int) {acc}" : acc)})";

    private static string ReadCall(Wire w, string stream) =>
        w.IsEnum ? $"({w.CsType}) {stream}.ReadEnum()" : $"{stream}.{w.Read}()";

    private static string Omit(Wire w, FType type, string acc)
    {
        if (type is FType.TypeString or FType.TypeBytes) return $"{acc}.Length != 0";
        if (type == FType.TypeBool) return acc;
        if (w.IsEnum) return $"(int) {acc} != 0";

        return $"{acc} != 0";
    }

    // ---- field classification ----------------------------------------------

    internal static bool IsMap(FieldDescriptorProto field, Resolver resolve, out DescriptorProto? entry)
    {
        entry = null;
        if (field.label != Label.LabelRepeated || field.type != FType.TypeMessage) return false;

        var d = resolve(field.TypeName);

        if (d?.Options?.MapEntry == true)
        {
            entry = d;
            return true;
        }

        return false;
    }

    private static string ElemCsType(FieldDescriptorProto field, string baseNs, CsName csNames)
    {
        if (field.type == FType.TypeMessage) return $"global::{baseNs}.{TypePath(field.TypeName, csNames)}";
        if (field.type == FType.TypeEnum) return $"global::{baseNs}.{TypePath(field.TypeName, csNames)}";

        return Scalar(field.type, "").CsType;
    }

    private static string Kind(FType type) => type switch {
        FType.TypeDouble => "Double",
        FType.TypeFloat => "Float",
        FType.TypeInt64 => "Int64",
        FType.TypeUint64 => "UInt64",
        FType.TypeInt32 => "Int32",
        FType.TypeFixed64 => "Fixed64",
        FType.TypeFixed32 => "Fixed32",
        FType.TypeBool => "Bool",
        FType.TypeString => "String",
        FType.TypeBytes => "Bytes",
        FType.TypeUint32 => "UInt32",
        FType.TypeSfixed32 => "SFixed32",
        FType.TypeSfixed64 => "SFixed64",
        FType.TypeSint32 => "SInt32",
        FType.TypeSint64 => "SInt64",
        FType.TypeEnum => "Enum",
        FType.TypeMessage => "Message",
        _ => throw new InvalidOperationException($"Unsupported proto type for descriptor: {type}")
    };
}
