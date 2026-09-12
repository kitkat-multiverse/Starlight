using Starlight.Protobuf.Compiler;
using Xunit;

namespace Starlight.Protobuf.Tests;

/// <summary>
///     Unit tests for the compiler's reserved-name rules (<see cref="ReservedNames" />),
///     the pure core behind the SLPB003 diagnostic. Verbatim-emitted names (type and
///     enum-value names) must reject C# keywords; field property names must reject
///     collisions with emitter-generated members.
/// </summary>
public sealed class ReservedNameTests
{
    [Theory]
    [InlineData("class")]
    [InlineData("event")]
    [InlineData("int")]
    [InlineData("static")]
    [InlineData("lock")]
    [InlineData("default")]
    public void Keyword_IsRejected(string name)
    {
        Assert.True(ReservedNames.IsReservedKeyword(name));
        Assert.NotNull(ReservedNames.CheckKeyword("message", name));
    }

    [Theory]
    [InlineData("Class")] // PascalCased -- the emitter's property path is already safe
    [InlineData("value")] // contextual keyword, legal identifier
    [InlineData("var")]
    [InlineData("Message")]
    public void NonReservedName_IsAccepted(string name)
    {
        Assert.False(ReservedNames.IsReservedKeyword(name));
        Assert.Null(ReservedNames.CheckKeyword("message", name));
    }

    [Fact]
    public void CheckKeyword_CarriesKindAndVerbatimName()
    {
        var v = ReservedNames.CheckKeyword("enum value", "default");
        Assert.NotNull(v);
        Assert.Equal("enum value", v!.Value.Kind);
        Assert.Equal("default", v.Value.ProtoName);
        Assert.Equal("default", v.Value.CsName);
        Assert.Contains("keyword", v.Value.Reason);
    }

    [Fact]
    public void Field_CollidingWithUnknownFields_IsRejected()
    {
        var v = ReservedNames.GeneratedMemberCollisions(
            "M", hasCmdId: false, [], ["unknown_fields"]);

        var hit = Assert.Single(v);
        Assert.Equal("field", hit.Kind);
        Assert.Equal("unknown_fields", hit.ProtoName);
        Assert.Equal("UnknownFields", hit.CsName);
    }

    [Fact]
    public void Field_CollidingWithCmdId_OnlyWhenMessageHasCmdId()
    {
        Assert.Empty(ReservedNames.GeneratedMemberCollisions(
            "M", hasCmdId: false, [], ["cmd_id"]));

        var v = ReservedNames.GeneratedMemberCollisions(
            "M", hasCmdId: true, [], ["cmd_id"]);
        Assert.Equal("CmdId", Assert.Single(v).CsName);
    }

    [Theory]
    [InlineData("choice_case", "ChoiceCase")]
    [InlineData("clear_choice", "ClearChoice")]
    public void Field_CollidingWithOneofGeneratedMember_IsRejected(string field, string csName)
    {
        var v = ReservedNames.GeneratedMemberCollisions(
            "M", hasCmdId: false, ["choice"], [field]);

        Assert.Equal(csName, Assert.Single(v).CsName);
    }

    [Fact]
    public void Field_NotCollidingWithAbsentOneof_IsAccepted()
    {
        Assert.Empty(ReservedNames.GeneratedMemberCollisions(
            "M", hasCmdId: false, [], ["choice_case", "regular_field"]));
    }

    [Fact]
    public void Field_MatchingEnclosingType_IsNotAGeneratedMemberCollision()
    {
        // Prop suffixes "_" on a type-name collision, so the field clears the generated set.
        Assert.Empty(ReservedNames.GeneratedMemberCollisions(
            "Unknown", hasCmdId: false, [], ["unknown"]));
    }
}
