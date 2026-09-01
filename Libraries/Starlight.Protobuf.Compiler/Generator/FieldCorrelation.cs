using System.Collections.Generic;

namespace Starlight.Protobuf.Compiler;

/// <summary>
/// A base field's type diverging from the same-named version field. The emitted
/// serializer derives its wire codec from the base type while taking the wire
/// number from the version, so a divergence silently (de)serializes with the
/// wrong wire format -- the SLPB004 diagnostic.
/// </summary>
public readonly struct FieldTypeMismatch
{
    public FieldTypeMismatch(string fieldName, string baseType, string versionType)
    {
        FieldName = fieldName;
        BaseType = baseType;
        VersionType = versionType;
    }

    /// <summary>The proto field name (shared by both sides; correlation is by name).</summary>
    public string FieldName { get; }

    /// <summary>Human-readable base-side type, e.g. <c>int32</c> or <c>repeated message Foo</c>.</summary>
    public string BaseType { get; }

    /// <summary>Human-readable version-side type.</summary>
    public string VersionType { get; }
}

/// <summary>
/// The comparable shape of a proto field, decoupled from the descriptor library so
/// the correlation rule stays unit-testable (the generator's protobuf dependency is
/// private and does not flow to the test project -- see <see cref="ReservedNames"/>).
/// </summary>
public readonly struct FieldShape
{
    /// <param name="name">proto field name</param>
    /// <param name="protoType">proto type keyword for scalars (<c>int32</c>, <c>string</c>, ...) or the category (<c>message</c>, <c>enum</c>, <c>group</c>) for named types</param>
    /// <param name="repeated">true when the field carries the <c>repeated</c> label</param>
    /// <param name="typeName">simple (unqualified) referent name for named types; empty for scalars</param>
    public FieldShape(string name, string protoType, bool repeated, string typeName = "")
    {
        Name = name;
        ProtoType = protoType;
        Repeated = repeated;
        TypeName = typeName;
    }

    public string Name { get; }
    public string ProtoType { get; }
    public bool Repeated { get; }
    public string TypeName { get; }
}

/// <summary>
/// Pure base-vs-version field correlation rule shared by the generator's validation
/// pass and its tests. Fields are matched by name; a match requires identical proto
/// type, identical label, and -- for named types -- identical simple referent name.
/// Referents are compared by simple name because base and version live in different
/// packages (the emitter likewise keys on the simple name).
/// </summary>
public static class FieldCorrelation
{
    /// <summary>Type mismatches among fields present in both messages, in base field order.</summary>
    public static IReadOnlyList<FieldTypeMismatch> Mismatches(
        IEnumerable<FieldShape> baseFields,
        IEnumerable<FieldShape> versionFields
    )
    {
        // First occurrence wins: de-obfuscated version protos can carry duplicate field
        // names, and ToDictionary would throw. Mirrors CodeEmitter.FieldsByName.
        var versionByName = new Dictionary<string, FieldShape>();

        foreach (var f in versionFields)
        {
            if (!versionByName.ContainsKey(f.Name)) versionByName[f.Name] = f;
        }
        var mismatches = new List<FieldTypeMismatch>();

        foreach (var b in baseFields)
        {
            if (!versionByName.TryGetValue(b.Name, out var v)) continue;
            if (Matches(b, v)) continue;

            mismatches.Add(new FieldTypeMismatch(b.Name, Describe(b), Describe(v)));
        }

        return mismatches;
    }

    /// <summary>
    /// Compares an already-paired base and version field (the pairing may be by name or by
    /// an <c>alts</c> alternate), returning the mismatch or null when their wire shapes agree.
    /// The reported field name is the base field's.
    /// </summary>
    public static FieldTypeMismatch? Compare(FieldShape baseField, FieldShape versionField) =>
        Matches(baseField, versionField) ? null : new FieldTypeMismatch(baseField.Name, Describe(baseField), Describe(versionField));

    private static bool Matches(FieldShape a, FieldShape b) =>
        a.ProtoType == b.ProtoType && a.Repeated == b.Repeated && a.TypeName == b.TypeName;

    /// <summary>Renders a field's type as it would read in a .proto, e.g. <c>repeated message Foo</c>.</summary>
    public static string Describe(FieldShape f)
    {
        var core = f.TypeName.Length > 0 ? $"{f.ProtoType} {f.TypeName}" : f.ProtoType;
        return f.Repeated ? $"repeated {core}" : core;
    }
}
