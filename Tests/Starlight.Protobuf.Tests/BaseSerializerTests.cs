using Starlight.Protobuf.Core;
using Starlight.Protobuf.Fixtures;
using Xunit;

namespace Starlight.Protobuf.Tests;

/// <summary>
/// Base serializers: the canonical, version-independent wire format used for
/// server-to-server exchange. Unlike the per-version serializers, a base message
/// serializes with its own structural field numbers via the argument-free
/// <see cref="MessageExtensions.ToByteArray{T}(T)"/> / <c>MergeFrom</c> path, so any
/// server holding the base contract can decode it losslessly.
/// </summary>
public sealed class BaseSerializerTests
{
    [Fact]
    public void ToByteArray_UsesBaseStructuralFieldNumbers()
    {
        // PingReq.seq is base structural field 2 (it is wire field 4 in v99), so the
        // canonical encode tags it (2<<3)|0 = 0x10 -- proving this is the base format,
        // not the version one.
        var bytes = new PingReq { Seq = 150 }.ToByteArray();

        byte[] expected = [0x10, 0x96, 0x01]; // tag 0x10, value 150 (varint 0x96 0x01)
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void ToByteArray_OmitsProto3DefaultValues()
    {
        Assert.Empty(new PingReq().ToByteArray());
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = new PingReq {
            ClientId = "client-123",
            Seq = 42,
            Tags = { 7, 8, 9 },
            Flag = true
        };

        var bytes = original.ToByteArray();
        var restored = new PingReq();
        restored.MergeFrom(bytes);

        Assert.Equal(original.ClientId, restored.ClientId);
        Assert.Equal(original.Seq, restored.Seq);
        Assert.Equal(original.Tags, restored.Tags);
        Assert.Equal(original.Flag, restored.Flag);
    }

    [Fact]
    public void RoundTrip_PreservesOptionalAndOneof()
    {
        var original = new Coverage {
            OptInt = 0, // explicit presence: a set zero must survive
            Plain = 5,
            ChoiceMsg = new CoverageSub { Value = 99 }
        };

        var bytes = original.ToByteArray();
        var restored = new Coverage();
        restored.MergeFrom(bytes);

        Assert.Equal(expected: 0, restored.OptInt);
        Assert.Equal(original.Plain, restored.Plain);
        Assert.Equal(Coverage.ChoiceOneofCase.ChoiceMsg, restored.ChoiceCase);
        Assert.Equal(expected: 99, restored.ChoiceMsg!.Value);
    }

    [Fact]
    public void BaseFormat_DiffersFromVersionFormat()
    {
        var message = new PingReq { Seq = 150 };

        // The same message encodes differently under the canonical base format
        // (field 2) and the v99 wire format (field 4) -- the whole point of keeping
        // a separate base serializer for server-to-server traffic.
        Assert.NotEqual(message.ToByteArray(), message.ToByteArray(Fixtures.V99.PingReqSerializer.Instance));
    }
}
