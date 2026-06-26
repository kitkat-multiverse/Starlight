using System.Linq;
using System.Text;
using Google.Protobuf.Reflection;
using FType = Google.Protobuf.Reflection.FieldDescriptorProto.Type;
using Label = Google.Protobuf.Reflection.FieldDescriptorProto.Label;

namespace Starlight.Protobuf.Compiler;

internal static partial class CodeEmitter
{
    // ---- descriptor emission (reflective engine / remap field table) --------

    private const string FdType = "global::Starlight.Protobuf.Core.FieldDescriptor";
    private const string PkType = "global::Starlight.Protobuf.Core.ProtoKind";
    private const string FrType = "global::Starlight.Protobuf.Core.FieldRule";

    /// <summary>
    /// Emits the per-message field table that drives the reflective slow path and
    /// field-ID remap. Only name-matched fields are included, mirroring the fast
    /// path; nested message references are lazy (<c>() => XSerializer.Descriptor</c>)
    /// to sidestep static-init ordering.
    /// </summary>
    private static void EmitDescriptor(
        StringBuilder sb,
        DescriptorProto baseMsg,
        DescriptorProto versionMsg,
        string baseNs,
        Resolver resolve,
        CsName csNames,
        TransformTable? transforms,
        AltsTable? alts = null,
        string? csPath = null
    )
    {
        var versionByName = FieldsByName(versionMsg.Fields);
        var type = $"global::{baseNs}.{csPath ?? StripPrefix(baseMsg.Name)}";

        sb.AppendLine($"    public static readonly global::Starlight.Protobuf.Core.MessageDescriptor Descriptor =");
        sb.AppendLine($"        new global::Starlight.Protobuf.Core.MessageDescriptor(\"{baseMsg.Name}\", typeof({type}), new {FdType}[]");
        sb.AppendLine("        {");

        foreach (var field in baseMsg.Fields)
        {
            if (IsUnknownPlaceholder(field)) continue; // unknown placeholder -> round-trips via UnknownFields

            var vf = MatchVersionField(field, baseMsg.Name, versionByName, alts);
            if (vf is null) continue;

            var transform = transforms?.Get(versionMsg.Name, vf.Name);
            sb.AppendLine($"            {FieldDescriptorExpr(field, vf.Number, baseMsg, resolve, csNames, transform)},");
        }

        sb.AppendLine("        });");
        sb.AppendLine();
    }

    private static string FieldDescriptorExpr(
        FieldDescriptorProto field,
        int number,
        DescriptorProto msg,
        Resolver resolve,
        CsName csNames,
        Transform? transform = null
    )
    {
        var prop = Prop(field.Name, msg.Name);
        var head = $"new {FdType}(\"{field.Name}\", \"{prop}\", {field.Number}, {number}";

        if (IsMap(field, resolve, out var entry))
        {
            var keyField = entry!.Fields.First(f => f.Number == 1);
            var valField = entry.Fields.First(f => f.Number == 2);
            var extra = $", keyKind: {PkType}.{Kind(keyField.type)}";

            if (valField.type == FType.TypeMessage)
                extra += $", messageRef: () => {SerBase(TypePath(valField.TypeName, csNames))}Serializer.Descriptor";
            return $"{head}, {PkType}.{Kind(valField.type)}, {FrType}.Map{extra})";
        }

        if (field.label == Label.LabelRepeated)
        {
            var extra = field.type == FType.TypeMessage ?
                $", messageRef: () => {SerBase(TypePath(field.TypeName, csNames))}Serializer.Descriptor" :
                "";
            return $"{head}, {PkType}.{Kind(field.type)}, {FrType}.Repeated{extra})";
        }

        string rule;
        var named = "";

        if (InRealOneof(field))
        {
            rule = "Single";
            named = $", oneofName: \"{OneofName(msg, field)}\"";
        } else
        {
            rule = IsProto3Optional(field) ? "Optional" : "Single";
        }

        if (field.type == FType.TypeMessage)
            named += $", messageRef: () => {SerBase(TypePath(field.TypeName, csNames))}Serializer.Descriptor";

        // Every surviving transform is an invertible op-chain (add/xor/fop and parseable
        // masks); non-invertible masks are rejected at compile time, so the reflective
        // path always has a faithful runtime representation.
        if (transform is not null)
            named +=
                $", transform: new global::Starlight.Protobuf.Core.FieldTransform(\"{transform.Ops}\", new long[] {{ {string.Join(", ", transform.Operands)} }})";

        return $"{head}, {PkType}.{Kind(field.type)}, {FrType}.{rule}{named})";
    }
}
