using Google.Protobuf;
using Starlight.Protobuf.Core;
using Starlight.Protobuf.Fixtures;
using Starlight.Protobuf.Fixtures.V99;
using Xunit;

namespace Starlight.Protobuf.Tests;

/// <summary>
/// Field value transforms (proto <c>add</c>/<c>xor</c>/<c>fop</c>/<c>mask</c> options).
/// Proves the generated fast path writes the obfuscated wire value and inverts it on
/// read, that the reflective slow path produces byte-identical output, and that both
/// round-trip losslessly. Expected wire values are recomputed by hand here so the test
/// is an independent oracle, not a mirror of the generated arithmetic.
/// </summary>
public sealed class TransformTests
{
    private static readonly TransformedSerializer Serializer = TransformedSerializer.Instance;
    private static MessageDescriptor Descriptor => TransformedSerializer.Descriptor;

    private static Transformed Sample() => new() { A = 7, B = 1000, C = 42, D = 5, E = 1000 };

    // Independent reimplementation of the four proto declarations.
    private static uint EncA(uint v) => unchecked((uint)((long)v + 100 ^ 12345)); // fop=add
    private static ulong EncB(ulong v) => unchecked((ulong)(((long)v ^ 999) + 4242)); // fop=xor
    private static int EncC(int v) => unchecked((int)((long)v - 7 ^ 555)); // mask
    private static uint EncD(uint v) => unchecked((uint)((long)v + 50000)); // add only
    private static int EncE(int v) => unchecked((int)((long)v + -123 ^ 9000)); // negative add

    [Fact]
    public void FastPath_WritesObfuscatedWireValues()
    {
        var bytes = Sample().ToByteArray(Serializer);
        using var input = new CodedInputStream(bytes);

        Assert.Equal(EncA(7), ReadVarintField(input, expectedNumber: 1, () => input.ReadUInt32()));
        Assert.Equal(EncB(1000), ReadVarintField(input, expectedNumber: 2, () => input.ReadUInt64()));
        Assert.Equal(EncC(42), ReadVarintField(input, expectedNumber: 3, () => input.ReadInt32()));
        Assert.Equal(EncD(5), ReadVarintField(input, expectedNumber: 4, () => input.ReadUInt32()));
        Assert.Equal(EncE(1000), ReadVarintField(input, expectedNumber: 5, () => input.ReadInt32()));
        Assert.True(input.IsAtEnd);
    }

    [Fact]
    public void FastPath_RoundTrips()
    {
        var original = Sample();
        var restored = new Transformed();
        restored.MergeFrom(Serializer, original.ToByteArray(Serializer));

        Assert.Equal(original.A, restored.A);
        Assert.Equal(original.B, restored.B);
        Assert.Equal(original.C, restored.C);
        Assert.Equal(original.D, restored.D);
        Assert.Equal(original.E, restored.E);
    }

    [Fact]
    public void DefaultValuedTransformedField_IsOmitted()
    {
        // The presence guard tests the *real* value, so a real 0 is not written even
        // though its encoded form (e.g. 0 + 50000) is non-zero.
        var bytes = new Transformed().ToByteArray(Serializer);
        Assert.Empty(bytes);

        var restored = new Transformed();
        restored.MergeFrom(Serializer, bytes);
        Assert.Equal(expected: 0u, restored.D);
    }

    [Fact]
    public void ReflectivePath_IsByteIdenticalToFastPath()
    {
        var message = Sample();
        var fast = message.ToByteArray(Serializer);

        try
        {
            foreach (var f in Descriptor.Fields)
            {
                Assert.True(Descriptor.Remap(f.Name, f.DefaultNumber));
            }
            Assert.True(Descriptor.HasRemaps);

            Assert.Equal(fast, message.ToByteArray(Serializer));
            Assert.Equal(fast.Length, Serializer.CalculateSize(message));
        }
        finally
        {
            Descriptor.ClearRemaps();
        }
    }

    [Fact]
    public void ReflectivePath_RoundTrips()
    {
        var original = Sample();

        try
        {
            foreach (var f in Descriptor.Fields)
            {
                Descriptor.Remap(f.Name, f.DefaultNumber);
            }

            var restored = new Transformed();
            restored.MergeFrom(Serializer, original.ToByteArray(Serializer));

            Assert.Equal(original.A, restored.A);
            Assert.Equal(original.B, restored.B);
            Assert.Equal(original.C, restored.C);
            Assert.Equal(original.D, restored.D);
            Assert.Equal(original.E, restored.E);
        }
        finally
        {
            Descriptor.ClearRemaps();
        }
    }

    private static T ReadVarintField<T>(CodedInputStream input, int expectedNumber, Func<T> read)
    {
        var tag = input.ReadTag();
        Assert.Equal(expectedNumber, WireFormat.GetTagFieldNumber(tag));
        Assert.Equal(WireFormat.WireType.Varint, WireFormat.GetTagWireType(tag));
        return read();
    }
}
