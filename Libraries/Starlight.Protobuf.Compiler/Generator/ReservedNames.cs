using System.Collections.Generic;
using System.Linq;

namespace Starlight.Protobuf.Compiler;

/// <summary>A proto name that would generate uncompilable C#, with the reason why.</summary>
public readonly struct NameViolation
{
    public NameViolation(string kind, string protoName, string csName, string reason)
    {
        Kind = kind;
        ProtoName = protoName;
        CsName = csName;
        Reason = reason;
    }

    /// <summary>The proto construct: "message", "enum", "enum value", or "field".</summary>
    public string Kind { get; }

    /// <summary>The name as written in the .proto.</summary>
    public string ProtoName { get; }

    /// <summary>The C# identifier it generates.</summary>
    public string CsName { get; }

    /// <summary>Why it's rejected (keyword, or which member it collides with).</summary>
    public string Reason { get; }
}

/// <summary>
/// Pure name-collision rules shared by the generator's validation pass and its
/// tests. Keyword checks apply to names the emitter writes verbatim (type and
/// enum-value names); generated-member checks apply to field property names.
/// </summary>
public static class ReservedNames
{
    // Only *reserved* keywords matter: contextual keywords (var, value, async...)
    // are legal identifiers, and PascalCasing already keeps generated property /
    // enum-member names clear of this all-lowercase set. So this bites the names
    // emitted verbatim, where proto permits a lowercase keyword.
    private static readonly HashSet<string> Keywords = [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
        "checked", "class", "const", "continue", "decimal", "default", "delegate",
        "do", "double", "else", "enum", "event", "explicit", "extern", "false",
        "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
        "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
        "new", "null", "object", "operator", "out", "override", "params", "private",
        "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
        "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
    ];

    public static bool IsReservedKeyword(string name) => Keywords.Contains(name);

    /// <summary>A verbatim-emitted name (type or enum value) that collides with a C# keyword, else null.</summary>
    public static NameViolation? CheckKeyword(string kind, string name) => CheckKeyword(kind, name, name);

    /// <summary>
    /// As <see cref="CheckKeyword(string,string)"/> but checks the emitted C# identifier
    /// (<paramref name="csName"/>, e.g. with a prefix stripped) while reporting the raw
    /// proto name (<paramref name="protoName"/>).
    /// </summary>
    public static NameViolation? CheckKeyword(string kind, string protoName, string csName) =>
        IsReservedKeyword(csName) ? new NameViolation(kind, protoName, csName, "is a reserved C# keyword") : null;

    /// <summary>
    /// Field property names that collide with a member the emitter synthesizes on the
    /// same message: <c>UnknownFields</c>, the optional <c>CmdId</c> const, the
    /// <c>Serializer</c> static (version-independent messages only), and per real
    /// oneof the <c>{Name}Case</c> property, <c>Clear{Name}</c> method, and
    /// <c>{Name}OneofCase</c> enum. <paramref name="realOneofNames"/> are raw proto oneof
    /// names (Pascaled here to match emission).
    /// </summary>
    public static IReadOnlyList<NameViolation> GeneratedMemberCollisions(
        string messageName,
        bool hasCmdId,
        IEnumerable<string> realOneofNames,
        IEnumerable<string> fieldNames,
        bool selfSerializable = false
    )
    {
        var generated = new HashSet<string> { "UnknownFields" };
        if (hasCmdId) generated.Add("CmdId");
        if (selfSerializable) generated.Add("Serializer");

        foreach (var oneof in realOneofNames)
        {
            var p = CodeEmitter.Pascal(oneof);
            generated.Add($"{p}Case");
            generated.Add($"Clear{p}");
            generated.Add($"{p}OneofCase");
        }

        var violations = new List<NameViolation>();

        foreach (var field in fieldNames)
        {
            var prop = CodeEmitter.Prop(field, messageName);

            if (generated.Contains(prop))
                violations.Add(new NameViolation("field", field, prop,
                    $"collides with a compiler-generated member of message '{messageName}'"));
        }

        return violations;
    }
}
