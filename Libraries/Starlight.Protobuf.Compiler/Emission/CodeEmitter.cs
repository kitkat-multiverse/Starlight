using Google.Protobuf.Reflection;
using System.Collections.Generic;
using System.Text;

namespace Starlight.Protobuf.Compiler;

/// <summary>
/// Emits POCOs, per-version hardcoded serializers, and version registries from
/// parsed proto descriptors. Covers proto3 scalars, enums, bytes, nested
/// messages, packed/unpacked repeated, maps, explicit proto3 <c>optional</c>
/// presence, and <c>oneof</c>.
/// </summary>
internal static partial class CodeEmitter
{
    /// <summary>Maps a message's fully-qualified proto name to its descriptor (for map-entry / nested resolution).</summary>
    internal delegate DescriptorProto? Resolver(string fullyQualifiedTypeName);

    /// <summary>
    /// Maps a fully-qualified proto message/enum name to its dotted C# type path relative to the
    /// emitted namespace (e.g. <c>.CreateGadgetInfo.Chest</c> -> <c>CreateGadgetInfo.Chest</c>),
    /// with each path segment prefix-stripped. Returns null for types the map doesn't know.
    /// </summary>
    internal delegate string? CsName(string fullyQualifiedTypeName);

    // ---- presence classification --------------------------------------------

    /// <summary>True for a proto3 <c>optional</c> field (explicit presence via a synthetic one-field oneof).</summary>
    private static bool IsProto3Optional(FieldDescriptorProto f) => f.Proto3Optional;

    /// <summary>True for a field inside a real (user-declared) <c>oneof</c> -- excludes synthetic proto3-optional oneofs.</summary>
    private static bool InRealOneof(FieldDescriptorProto f) => f.ShouldSerializeOneofIndex() && !f.Proto3Optional;

    /// <summary>
    /// True for a field typed as the <c>UnknownMessage</c>/<c>UnknownType</c> placeholder. Such a
    /// type is a deliberate "we don't model this" stand-in, so the field is omitted from the
    /// emitted serializer and reflective descriptor entirely; its wire bytes round-trip
    /// losslessly through the message-level unknown-field set instead. A field whose type is
    /// genuinely known in a version but only a placeholder in base should be reconciled in the
    /// base proto, not left as a placeholder.
    /// </summary>
    internal static bool IsUnknownPlaceholder(FieldDescriptorProto f)
    {
        if (f.type != FieldDescriptorProto.Type.TypeMessage) return false;

        var simple = Simple(f.TypeName);
        return simple is "UnknownMessage" or "UnknownType";
    }

    private static string OneofName(DescriptorProto msg, FieldDescriptorProto f) => Pascal(msg.OneofDecls[f.OneofIndex].Name);

    /// <summary>
    /// Indexes a message's fields by prefix-stripped proto name for base/version correlation.
    /// The leading '_' custom-name marker is stripped here exactly as it is for type
    /// identifiers, so a version field <c>_score_board_list</c> correlates to base
    /// <c>score_board_list</c>. First occurrence wins: de-obfuscated version protos can carry
    /// duplicate (stripped) field names (a rename mask collapsing two obfuscated identifiers,
    /// or a real <c>foo</c> alongside a marked <c>_foo</c>), and a plain <c>ToDictionary</c>
    /// would throw and abort the whole generator. The first field is the real correlation
    /// target (the duplicate is an extra version-only field absent from base).
    /// </summary>
    internal static Dictionary<string, FieldDescriptorProto> FieldsByName(IEnumerable<FieldDescriptorProto> fields)
    {
        var map = new Dictionary<string, FieldDescriptorProto>();

        foreach (var f in fields)
        {
            var key = StripPrefix(f.Name);
            if (!map.ContainsKey(key)) map[key] = f;
        }

        return map;
    }

    // ---- naming -------------------------------------------------------------

    public static string Pascal(string snake)
    {
        var sb = new StringBuilder(snake.Length);
        var upper = true;

        foreach (var c in snake)
        {
            if (c == '_')
            {
                upper = true;
                continue;
            }

            sb.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }

        return sb.ToString();
    }

    public static string Camel(string snake)
    {
        var p = Pascal(snake);
        return p.Length == 0 ? p : char.ToLowerInvariant(p[0]) + p.Substring(1);
    }

    public static string Simple(string typeName)
    {
        var name = typeName.TrimStart('.');
        var dot = name.LastIndexOf('.');
        return dot < 0 ? name : name.Substring(dot + 1);
    }

    /// <summary>
    /// Strips a leading underscore prefix from a name so the emitted C# identifier reads
    /// cleanly (e.g. <c>_PlayerWorldInfo</c> -> <c>PlayerWorldInfo</c>). Stripping is for the
    /// C# identifier only -- proto correlation, transform keys and wire names keep the raw
    /// name. Skipped when it would leave an empty name or one starting with a digit, to keep
    /// the result a legal identifier.
    /// </summary>
    public static string StripPrefix(string name)
    {
        var i = 0;
        while (i < name.Length && name[i] == '_') i++;
        if (i == 0 || i >= name.Length || char.IsDigit(name[i])) return name;

        return name.Substring(i);
    }

    /// <summary>C# type identifier for a proto message/enum reference: simple name, prefix stripped.</summary>
    public static string TypeIdent(string typeName) => StripPrefix(Simple(typeName));

    /// <summary>
    /// Dotted C# type path for a proto message/enum reference, including the enclosing-message
    /// nesting (e.g. <c>CreateGadgetInfo.Chest</c>). Falls back to the leaf identifier when the
    /// type isn't in the resolver -- which only happens for top-level types, where the two agree.
    /// </summary>
    public static string TypePath(string typeName, CsName? csNames) => csNames?.Invoke(typeName) ?? TypeIdent(typeName);

    /// <summary>
    /// Serializer class base name for a (possibly nested) C# type path. Serializers are emitted as
    /// top-level classes, so a nested type's path is flattened with '_' to keep the name unique
    /// (e.g. <c>CreateGadgetInfo.Chest</c> -> <c>CreateGadgetInfo_Chest</c>).
    /// </summary>
    public static string SerBase(string typePath) => typePath.Replace(".", "_");

    /// <summary>
    /// Property name for a field. C# forbids a member sharing its enclosing type's
    /// name, so (matching protoc's C# generator) we suffix "_" when they collide.
    /// Compared against the message's emitted (prefix-stripped) C# name.
    /// </summary>
    public static string Prop(string fieldName, string messageName)
    {
        var p = Pascal(fieldName);
        return p == StripPrefix(messageName) ? p + "_" : p;
    }
}
