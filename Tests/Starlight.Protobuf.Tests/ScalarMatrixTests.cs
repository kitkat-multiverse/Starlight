using Google.Protobuf;
using Starlight.Protobuf.Core;
using Starlight.Protobuf.Fixtures;
using Xunit;

namespace Starlight.Protobuf.Tests;

/// <summary>
/// Exhaustive coverage: every proto3 scalar wire type in one message.
/// The generated serializer's bytes are compared against an independent oracle
/// built directly with <see cref="CodedOutputStream"/> (Google.Protobuf's wire
/// primitives), proving byte-parity, then round-tripped.
/// </summary>
public sealed class ScalarMatrixTests
{
    private static readonly Fixtures.V99.ScalarMatrixSerializer Serializer = Fixtures.V99.ScalarMatrixSerializer.Instance;

    private static ScalarMatrix Sample() => new() {
        FInt32 = 123,
        FInt64 = -456,
        FUint32 = 789,
        FUint64 = 1011,
        FSint32 = -1213,
        FSint64 = -1415,
        FFixed32 = 1617,
        FFixed64 = 1819,
        FSfixed32 = -1920,
        FSfixed64 = -2122,
        FBool = true,
        FFloat = 3.14f,
        FDouble = 2.71828,
        FString = "matrix",
        FBytes = ByteString.CopyFromUtf8("raw")
    };

    /// <summary>Encodes the sample using Google.Protobuf primitives + the V66 wire
    /// field numbers, in ascending order, as a parity oracle.</summary>
    private static byte[] Oracle(ScalarMatrix m)
    {
        using var stream = new MemoryStream();
        using var o = new CodedOutputStream(stream);
        o.WriteTag(fieldNumber: 101, WireFormat.WireType.Varint);
        o.WriteInt32(m.FInt32);
        o.WriteTag(fieldNumber: 102, WireFormat.WireType.Varint);
        o.WriteInt64(m.FInt64);
        o.WriteTag(fieldNumber: 103, WireFormat.WireType.Varint);
        o.WriteUInt32(m.FUint32);
        o.WriteTag(fieldNumber: 104, WireFormat.WireType.Varint);
        o.WriteUInt64(m.FUint64);
        o.WriteTag(fieldNumber: 105, WireFormat.WireType.Varint);
        o.WriteSInt32(m.FSint32);
        o.WriteTag(fieldNumber: 106, WireFormat.WireType.Varint);
        o.WriteSInt64(m.FSint64);
        o.WriteTag(fieldNumber: 107, WireFormat.WireType.Fixed32);
        o.WriteFixed32(m.FFixed32);
        o.WriteTag(fieldNumber: 108, WireFormat.WireType.Fixed64);
        o.WriteFixed64(m.FFixed64);
        o.WriteTag(fieldNumber: 109, WireFormat.WireType.Fixed32);
        o.WriteSFixed32(m.FSfixed32);
        o.WriteTag(fieldNumber: 110, WireFormat.WireType.Fixed64);
        o.WriteSFixed64(m.FSfixed64);
        o.WriteTag(fieldNumber: 111, WireFormat.WireType.Varint);
        o.WriteBool(m.FBool);
        o.WriteTag(fieldNumber: 112, WireFormat.WireType.Fixed32);
        o.WriteFloat(m.FFloat);
        o.WriteTag(fieldNumber: 113, WireFormat.WireType.Fixed64);
        o.WriteDouble(m.FDouble);
        o.WriteTag(fieldNumber: 114, WireFormat.WireType.LengthDelimited);
        o.WriteString(m.FString);
        o.WriteTag(fieldNumber: 115, WireFormat.WireType.LengthDelimited);
        o.WriteBytes(m.FBytes);
        o.Flush();
        return stream.ToArray();
    }

    [Fact]
    public void AllScalars_MatchGoogleProtobufBytes()
    {
        var message = Sample();
        Assert.Equal(Oracle(message), message.ToByteArray(Serializer));
    }

    [Fact]
    public void CalculateSize_MatchesActualLength()
    {
        var message = Sample();
        Assert.Equal(message.ToByteArray(Serializer).Length, Serializer.CalculateSize(message));
    }

    [Fact]
    public void AllScalars_RoundTrip()
    {
        var original = Sample();

        var restored = new ScalarMatrix();
        restored.MergeFrom(Serializer, original.ToByteArray(Serializer));

        Assert.Equal(original.FInt32, restored.FInt32);
        Assert.Equal(original.FInt64, restored.FInt64);
        Assert.Equal(original.FUint32, restored.FUint32);
        Assert.Equal(original.FUint64, restored.FUint64);
        Assert.Equal(original.FSint32, restored.FSint32);
        Assert.Equal(original.FSint64, restored.FSint64);
        Assert.Equal(original.FFixed32, restored.FFixed32);
        Assert.Equal(original.FFixed64, restored.FFixed64);
        Assert.Equal(original.FSfixed32, restored.FSfixed32);
        Assert.Equal(original.FSfixed64, restored.FSfixed64);
        Assert.Equal(original.FBool, restored.FBool);
        Assert.Equal(original.FFloat, restored.FFloat);
        Assert.Equal(original.FDouble, restored.FDouble);
        Assert.Equal(original.FString, restored.FString);
        Assert.Equal(original.FBytes, restored.FBytes);
    }
}
