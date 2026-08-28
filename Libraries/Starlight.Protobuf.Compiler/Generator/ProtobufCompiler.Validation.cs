using System.Collections.Generic;
using System.Linq;
using Google.Protobuf.Reflection;
using Microsoft.CodeAnalysis;
using FType = Google.Protobuf.Reflection.FieldDescriptorProto.Type;
using Label = Google.Protobuf.Reflection.FieldDescriptorProto.Label;

namespace Starlight.Protobuf.Compiler;

public sealed partial class ProtobufCompiler
{
    /// <summary>
    /// Rejects proto names that would generate uncompilable C#: verbatim-emitted type
    /// and enum-value names that collide with a reserved keyword, and field names whose
    /// property form collides with an emitter-synthesized member. The collision rules
    /// live in <see cref="ReservedNames"/>; this walks the descriptors and reports.
    /// We still emit (the bad C# fails to compile anyway) but SLPB003 names the cause.
    /// </summary>
    private static void ValidateNames(
        SourceProductionContext ctx,
        IEnumerable<DescriptorProto> messages,
        IEnumerable<EnumDescriptorProto> topLevelEnums,
        IReadOnlyDictionary<string, int> cmdIds,
        bool selfSerializable = false
    )
    {
        foreach (var e in topLevelEnums)
        {
            ValidateEnum(ctx, e);
        }

        foreach (var msg in messages)
        {
            ValidateMessage(ctx, msg, cmdIds.ContainsKey(msg.Name), selfSerializable);
        }
    }

    private static void ValidateMessage(SourceProductionContext ctx, DescriptorProto msg, bool hasCmdId, bool selfSerializable)
    {
        Report(ctx, ReservedNames.CheckKeyword("message", msg.Name, CodeEmitter.StripPrefix(msg.Name)));

        foreach (var e in msg.EnumTypes)
        {
            ValidateEnum(ctx, e);
        }

        var realOneofs = msg.Fields
            .Where(f => f.ShouldSerializeOneofIndex() && !f.Proto3Optional)
            .Select(f => msg.OneofDecls[f.OneofIndex].Name)
            .Distinct();

        foreach (var v in ReservedNames.GeneratedMemberCollisions(
                     msg.Name, hasCmdId, realOneofs, msg.Fields.Select(f => f.Name), selfSerializable))
        {
            Report(ctx, v);
        }
    }

    private static void ValidateEnum(SourceProductionContext ctx, EnumDescriptorProto e)
    {
        Report(ctx, ReservedNames.CheckKeyword("enum", e.Name, CodeEmitter.StripPrefix(e.Name)));

        foreach (var value in e.Values)
        {
            Report(ctx, ReservedNames.CheckKeyword("enum value", value.Name));
        }
    }

    private static void Report(SourceProductionContext ctx, NameViolation? violation)
    {
        if (violation is { } v)
            ctx.ReportDiagnostic(Diagnostic.Create(ReservedNameError, Location.None,
                v.Kind, v.ProtoName, v.CsName, v.Reason));
    }

    /// <summary>
    /// Flags base fields whose type diverges from the same-named version field. The
    /// emitter correlates fields by name and derives the wire codec from the base
    /// type, so a type divergence yields a serializer that reads/writes the wrong
    /// wire format with no compile error. The rule lives in <see cref="FieldCorrelation"/>.
    /// </summary>
    private static void ValidateFieldTypes(
        SourceProductionContext ctx,
        DescriptorProto baseMsg,
        DescriptorProto versionMsg,
        string version,
        CodeEmitter.AltsTable? alts
    )
    {
        var versionByName = CodeEmitter.FieldsByName(versionMsg.Fields);

        foreach (var bf in baseMsg.Fields)
        {
            if (CodeEmitter.IsUnknownPlaceholder(bf)) continue; // not emitted; round-trips via UnknownFields

            var vf = CodeEmitter.MatchVersionField(bf, baseMsg.Name, versionByName, alts);
            if (vf is null) continue;

            if (FieldCorrelation.Compare(Shape(bf), Shape(vf)) is { } m)
                ctx.ReportDiagnostic(Diagnostic.Create(FieldTypeMismatchError, Location.None,
                    m.FieldName, baseMsg.Name, m.BaseType, m.VersionType, version));
        }
    }

    /// <summary>
    /// Rejects value transforms on fields they cannot legally apply to. The serializer
    /// only encodes/decodes transforms on the singular-scalar path, so a transform on a
    /// repeated, map, or non-integer field is silently dropped at runtime -- the wire
    /// bytes would be the untransformed value. We fail the build (SLPB007) instead.
    /// Mirrors the consumption sites: keyed by <paramref name="versionMsg"/> name over
    /// the <paramref name="baseMsg"/> fields the serializer actually emits.
    /// </summary>
    private static void ValidateTransforms(
        SourceProductionContext ctx,
        DescriptorProto baseMsg,
        DescriptorProto versionMsg,
        CodeEmitter.TransformTable transforms,
        CodeEmitter.Resolver resolve,
        CodeEmitter.AltsTable? alts
    )
    {
        var versionByName = CodeEmitter.FieldsByName(versionMsg.Fields);

        foreach (var field in baseMsg.Fields)
        {
            var vf = CodeEmitter.MatchVersionField(field, baseMsg.Name, versionByName, alts);
            if (vf is null) continue;
            if (transforms.Get(versionMsg.Name, vf.Name) is null) continue;

            var reason = TransformRejection(field, resolve);

            if (reason is not null)
                ctx.ReportDiagnostic(Diagnostic.Create(TransformUnsupportedError, Location.None,
                    field.Name, baseMsg.Name, reason));
        }
    }

    /// <summary>Reason the field can't carry a transform, or null if it can (singular integer).</summary>
    private static string? TransformRejection(FieldDescriptorProto field, CodeEmitter.Resolver resolve)
    {
        if (CodeEmitter.IsMap(field, resolve, out _)) return "a map field";
        if (field.label == Label.LabelRepeated) return "a repeated field";
        if (!CodeEmitter.IsTransformable(field.type)) return $"of non-integer type '{ProtoKeyword(field.type)}'";

        return null;
    }

    private static FieldShape Shape(FieldDescriptorProto f)
    {
        var repeated = f.label == Label.LabelRepeated;

        // Referents compared by prefix-stripped simple name: the '_' custom-name marker
        // is stripped in emission, so base 'AkaFesDetailInfo' and version '_AkaFesDetailInfo'
        // denote the same C# type and must not be flagged as a divergence.
        return f.type switch {
            FType.TypeMessage => new FieldShape(f.Name, "message", repeated, CodeEmitter.TypeIdent(f.TypeName)),
            FType.TypeEnum => new FieldShape(f.Name, "enum", repeated, CodeEmitter.TypeIdent(f.TypeName)),
            FType.TypeGroup => new FieldShape(f.Name, "group", repeated, CodeEmitter.TypeIdent(f.TypeName)),
            _ => new FieldShape(f.Name, ProtoKeyword(f.type), repeated)
        };
    }

    private static string ProtoKeyword(FType type) => type switch {
        FType.TypeDouble => "double",
        FType.TypeFloat => "float",
        FType.TypeInt64 => "int64",
        FType.TypeUint64 => "uint64",
        FType.TypeInt32 => "int32",
        FType.TypeFixed64 => "fixed64",
        FType.TypeFixed32 => "fixed32",
        FType.TypeBool => "bool",
        FType.TypeString => "string",
        FType.TypeBytes => "bytes",
        FType.TypeUint32 => "uint32",
        FType.TypeSfixed32 => "sfixed32",
        FType.TypeSfixed64 => "sfixed64",
        FType.TypeSint32 => "sint32",
        FType.TypeSint64 => "sint64",
        _ => type.ToString()
    };
}
