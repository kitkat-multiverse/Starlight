using Starlight.Protobuf.Compiler;
using Xunit;

namespace Starlight.Protobuf.Tests;

/// <summary>
///     Unit tests for the compiler's base-vs-version field correlation rule
///     (<see cref="FieldCorrelation" />), the pure core behind the SLPB004 diagnostic.
///     A base field whose type diverges from the same-named version field would make
///     the emitter generate a serializer with the wrong wire codec.
/// </summary>
public sealed class FieldCorrelationTests
{
    private static FieldShape Scalar(string name, string protoType, bool repeated = false) =>
        new(name, protoType, repeated);

    private static FieldShape Message(string name, string typeName, bool repeated = false) =>
        new(name, "message", repeated, typeName);

    [Fact]
    public void ScalarTypeDivergence_IsReported()
    {
        var m = FieldCorrelation.Mismatches(
            [Scalar("foo", "int32")], [Scalar("foo", "string")]);

        var hit = Assert.Single(m);
        Assert.Equal("foo", hit.FieldName);
        Assert.Equal("int32", hit.BaseType);
        Assert.Equal("string", hit.VersionType);
    }

    [Fact]
    public void WireCompatibleScalarDivergence_IsReported()
    {
        // "Exact type + label" strictness: int32 vs int64 is wire-compatible but the
        // base codec mis-decodes the value, so it must still be flagged.
        var m = FieldCorrelation.Mismatches(
            [Scalar("n", "int32")], [Scalar("n", "int64")]);

        Assert.Equal("int64", Assert.Single(m).VersionType);
    }

    [Fact]
    public void LabelDivergence_IsReported()
    {
        var m = FieldCorrelation.Mismatches(
            [Scalar("xs", "int32")], [Scalar("xs", "int32", repeated: true)]);

        var hit = Assert.Single(m);
        Assert.Equal("int32", hit.BaseType);
        Assert.Equal("repeated int32", hit.VersionType);
    }

    [Fact]
    public void IdenticalScalar_IsAccepted()
    {
        Assert.Empty(FieldCorrelation.Mismatches(
            [Scalar("foo", "int32")], [Scalar("foo", "int32")]));
    }

    [Fact]
    public void SamePackageQualifierAside_MessageReferentComparedBySimpleName()
    {
        // Base and version live in different packages, so referents are compared by
        // simple name -- the same message must not be a false positive.
        Assert.Empty(FieldCorrelation.Mismatches(
            [Message("bar", "Bar")], [Message("bar", "Bar")]));
    }

    [Fact]
    public void DifferentMessageReferent_IsReported()
    {
        var m = FieldCorrelation.Mismatches(
            [Message("bar", "Bar")], [Message("bar", "Baz")]);

        var hit = Assert.Single(m);
        Assert.Equal("message Bar", hit.BaseType);
        Assert.Equal("message Baz", hit.VersionType);
    }

    [Fact]
    public void EnumVersusMessage_SameReferentName_IsReported()
    {
        // Different categories (varint enum vs length-delimited message) must not be
        // masked by a coincidentally equal referent name.
        var m = FieldCorrelation.Mismatches(
            [new FieldShape("f", "enum", repeated: false, "Color")],
            [new FieldShape("f", "message", repeated: false, "Color")]);

        var hit = Assert.Single(m);
        Assert.Equal("enum Color", hit.BaseType);
        Assert.Equal("message Color", hit.VersionType);
    }

    [Fact]
    public void FieldAbsentFromOneSide_IsNotAMismatch()
    {
        // Presence differences are out of scope: the emitter simply skips fields the
        // other side lacks. Only the name intersection is type-checked.
        Assert.Empty(FieldCorrelation.Mismatches(
            [Scalar("a", "int32"), Scalar("b", "string")],
            [Scalar("a", "int32")]));

        Assert.Empty(FieldCorrelation.Mismatches(
            [Scalar("a", "int32")],
            [Scalar("a", "int32"), Scalar("c", "bool")]));
    }
}
