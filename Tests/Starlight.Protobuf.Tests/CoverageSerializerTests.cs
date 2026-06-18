using Starlight.Protobuf.Core;
using Starlight.Protobuf.Fixtures;
using Xunit;

namespace Starlight.Protobuf.Tests;

/// <summary>
/// Coverage: proto3 <c>optional</c> explicit presence, <c>oneof</c>
/// discriminated unions, and the version-independent (<c>independent.proto</c>)
/// path. The version dump shifts field numbers away from the base structural
/// ones, so these also prove name-match correlation survives presence/oneof.
/// </summary>
public sealed class CoverageSerializerTests
{
    private static readonly Fixtures.V99.CoverageSerializer Serializer = Fixtures.V99.CoverageSerializer.Instance;

    // ---- proto3 optional (explicit presence) --------------------------------

    [Fact]
    public void Optional_SetToDefaultValue_IsStillWritten()
    {
        // Presence means an explicitly-set zero must serialize. opt_int is wire
        // field 11 -> tag (11<<3)|0 = 0x58, value 0.
        var bytes = new Coverage { OptInt = 0 }.ToByteArray(Serializer);
        Assert.Equal(new byte[] { 0x58, 0x00 }, bytes);
    }

    [Fact]
    public void Optional_Unset_IsOmitted()
    {
        var bytes = new Coverage().ToByteArray(Serializer);
        Assert.Empty(bytes);
    }

    [Fact]
    public void Optional_RoundTrips_ValueAndPresence()
    {
        var original = new Coverage { OptInt = 42, OptStr = "hello", Plain = 7 };

        var restored = new Coverage();
        restored.MergeFrom(Serializer, original.ToByteArray(Serializer));

        Assert.Equal(expected: 42, restored.OptInt);
        Assert.Equal("hello", restored.OptStr);
        Assert.Equal(expected: 7, restored.Plain);
    }

    [Fact]
    public void Optional_AbsentOnWire_StaysNull()
    {
        var restored = new Coverage();
        restored.MergeFrom(Serializer, new Coverage { Plain = 1 }.ToByteArray(Serializer));

        Assert.Null(restored.OptInt);
        Assert.Null(restored.OptStr);
    }

    // ---- oneof --------------------------------------------------------------

    [Fact]
    public void Oneof_DefaultedActiveCase_IsStillWritten()
    {
        // choice_int active with value 0 must serialize -> wire field 15, tag 0x78.
        var msg = new Coverage { ChoiceInt = 0 };
        Assert.Equal(Coverage.ChoiceOneofCase.ChoiceInt, msg.ChoiceCase);
        Assert.Equal(new byte[] { 0x78, 0x00 }, msg.ToByteArray(Serializer));
    }

    [Fact]
    public void Oneof_Unset_WritesNothing()
    {
        var msg = new Coverage();
        Assert.Equal(Coverage.ChoiceOneofCase.None, msg.ChoiceCase);
        Assert.Empty(msg.ToByteArray(Serializer));
    }

    [Fact]
    public void Oneof_SettingOneCase_ClearsTheOthers()
    {
        var msg = new Coverage { ChoiceStr = "x" };
        Assert.Equal(Coverage.ChoiceOneofCase.ChoiceStr, msg.ChoiceCase);

        msg.ChoiceInt = 5;
        Assert.Equal(Coverage.ChoiceOneofCase.ChoiceInt, msg.ChoiceCase);
        Assert.Equal("", msg.ChoiceStr); // inactive case reads as default
        Assert.Equal(expected: 5, msg.ChoiceInt);
    }

    [Fact]
    public void Oneof_ScalarCase_RoundTrips()
    {
        var restored = new Coverage();
        restored.MergeFrom(Serializer, new Coverage { ChoiceStr = "picked" }.ToByteArray(Serializer));

        Assert.Equal(Coverage.ChoiceOneofCase.ChoiceStr, restored.ChoiceCase);
        Assert.Equal("picked", restored.ChoiceStr);
        Assert.Equal(expected: 0, restored.ChoiceInt);
    }

    [Fact]
    public void Oneof_MessageCase_RoundTrips()
    {
        var restored = new Coverage();

        restored.MergeFrom(Serializer,
            new Coverage { ChoiceMsg = new CoverageSub { Value = 99 } }.ToByteArray(Serializer));

        Assert.Equal(Coverage.ChoiceOneofCase.ChoiceMsg, restored.ChoiceCase);
        Assert.NotNull(restored.ChoiceMsg);
        Assert.Equal(expected: 99, restored.ChoiceMsg!.Value);
    }

    [Fact]
    public void Oneof_LastFieldOnWire_Wins()
    {
        // Concatenate two different oneof cases; proto semantics keep the last.
        var a = new Coverage { ChoiceStr = "first" }.ToByteArray(Serializer);
        var b = new Coverage { ChoiceInt = 123 }.ToByteArray(Serializer);

        var restored = new Coverage();
        restored.MergeFrom(Serializer, [.. a, .. b]);

        Assert.Equal(Coverage.ChoiceOneofCase.ChoiceInt, restored.ChoiceCase);
        Assert.Equal(expected: 123, restored.ChoiceInt);
    }

    // ---- version-independent (independent.proto) ----------------------------

    [Fact]
    public void Independent_Frame_RoundTrips()
    {
        var original = new Frame {
            SequenceId = 11,
            SentMs = 1717000000000,
            Flags = 6,
            Length = 2048,
            Index = 1,
            Total = 4
        };

        var restored = new Frame();
        restored.MergeFrom(FrameSerializer.Instance, original.ToByteArray(FrameSerializer.Instance));

        Assert.Equal(original.SequenceId, restored.SequenceId);
        Assert.Equal(original.SentMs, restored.SentMs);
        Assert.Equal(original.Flags, restored.Flags);
        Assert.Equal(original.Length, restored.Length);
        Assert.Equal(original.Index, restored.Index);
        Assert.Equal(original.Total, restored.Total);
    }

    [Fact]
    public void Independent_Frame_RoundTrips_WithoutSpecifyingSerializer()
    {
        // Version-independent messages own their single serializer, so the
        // argument-free extensions resolve it with no version lookup.
        var original = new Frame { SequenceId = 11, Flags = 6, Total = 4 };

        var restored = new Frame();
        restored.MergeFrom(original.ToByteArray());

        Assert.Equal(original.SequenceId, restored.SequenceId);
        Assert.Equal(original.Flags, restored.Flags);
        Assert.Equal(original.Total, restored.Total);
    }

    [Fact]
    public void Independent_SelfSerializer_MatchesExplicitInstance()
    {
        var msg = new Frame { Index = 7 };
        Assert.Equal(msg.ToByteArray(FrameSerializer.Instance), msg.ToByteArray());
        Assert.Same(FrameSerializer.Instance, Frame.Serializer);
    }

    [Fact]
    public void Independent_Crate_PropertyCollisionSuffix_RoundTrips()
    {
        // Field `crate` collides with message `Crate`, so the property is `Crate_`.
        var original = new Crate { Crate_ = Google.Protobuf.ByteString.CopyFromUtf8("payload") };

        var restored = new Crate();
        restored.MergeFrom(CrateSerializer.Instance, original.ToByteArray(CrateSerializer.Instance));

        Assert.Equal(original.Crate_, restored.Crate_);
    }
}
