using Google.Protobuf;
using Starlight.Protobuf.Core;
using Starlight.Protobuf.Fixtures;
using Starlight.Protobuf.Serialization;
using Xunit;

namespace Starlight.Protobuf.Tests;

/// <summary>
/// Gated remap. <see cref="RemapProbe"/> exercises every branch of the
/// shared <see cref="ReflectiveEngine"/> (scalars, optional, repeated packed /
/// unpacked, scalar &amp; message maps, enums, nested messages, oneof). These
/// tests prove: the reflective path is byte-identical to the fast path when no
/// number is changed, a remap actually rewrites the wire field number,
/// <see cref="MessageDescriptor.ClearRemaps"/> restores the fast path, and the
/// reflective path round-trips losslessly.
///
/// The serializer's <c>Descriptor</c> is a process-wide singleton, so every test
/// mutating it restores the defaults in a <c>finally</c> to stay isolated.
/// </summary>
public sealed class RemapTests
{
    private static readonly Fixtures.V99.RemapProbeSerializer Serializer = Fixtures.V99.RemapProbeSerializer.Instance;
    private static MessageDescriptor Descriptor => Fixtures.V99.RemapProbeSerializer.Descriptor;

    private static RemapProbe Sample() => new() {
        A = 123,
        B = "hello",
        C = -7,
        D = { 1, 2, 300 },
        E = { "x", "yy" },
        F = { [10] = 11, [12] = 13 },
        G = new RemapSub { N = 99 },
        H = { new RemapSub { N = 1 }, new RemapSub { N = 2 } },
        I = { [5] = new RemapSub { N = 50 } },
        En = RemapEnum.REMAP_TWO,
        Ens = { RemapEnum.REMAP_ONE, RemapEnum.REMAP_TWO },
        J = 4567
    };

    [Fact]
    public void ReflectivePath_IsByteIdenticalToFastPath_WhenNumbersUnchanged()
    {
        var message = Sample();
        var fast = message.ToByteArray(Serializer);

        try
        {
            // Remap every field to its own default number: flips on the reflective
            // path without changing any wire layout, so output must match the fast path.
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
    public void Remap_RewritesTheWireFieldNumber()
    {
        var message = new RemapProbe { A = 123 };

        try
        {
            Assert.True(Descriptor.Remap("a", wireNumber: 99));
            var bytes = message.ToByteArray(Serializer);

            // First (only) field on the wire must now carry field number 99, not 21.
            using var input = new CodedInputStream(bytes);
            var tag = input.ReadTag();
            Assert.Equal(expected: 99, WireFormat.GetTagFieldNumber(tag));
            Assert.Equal(WireFormat.WireType.Varint, WireFormat.GetTagWireType(tag));
            Assert.Equal(expected: 123, input.ReadInt32());
        }
        finally
        {
            Descriptor.ClearRemaps();
        }
    }

    [Fact]
    public void ClearRemaps_RestoresTheFastPath()
    {
        var message = Sample();
        var fast = message.ToByteArray(Serializer);

        Descriptor.Remap("a", wireNumber: 99);
        Assert.True(Descriptor.HasRemaps);
        Assert.NotEqual(fast, message.ToByteArray(Serializer));

        Descriptor.ClearRemaps();
        Assert.False(Descriptor.HasRemaps);
        Assert.Equal(fast, message.ToByteArray(Serializer));
    }

    [Fact]
    public void ReflectivePath_RoundTrips_AcrossEveryKind()
    {
        var original = Sample();

        try
        {
            foreach (var f in Descriptor.Fields)
            {
                Descriptor.Remap(f.Name, f.DefaultNumber);
            }

            var restored = new RemapProbe();
            restored.MergeFrom(Serializer, original.ToByteArray(Serializer));

            Assert.Equal(original.A, restored.A);
            Assert.Equal(original.B, restored.B);
            Assert.Equal(original.C, restored.C);
            Assert.Equal(original.D, restored.D);
            Assert.Equal(original.E, restored.E);
            Assert.Equal(original.F, restored.F);
            Assert.Equal(original.G!.N, restored.G!.N);
            Assert.Equal(original.H.Count, restored.H.Count);
            Assert.Equal(original.H[0].N, restored.H[0].N);
            Assert.Equal(original.H[1].N, restored.H[1].N);
            Assert.Equal(original.I[5].N, restored.I[5].N);
            Assert.Equal(original.En, restored.En);
            Assert.Equal(original.Ens, restored.Ens);
            Assert.Equal(RemapProbe.ChoiceOneofCase.J, restored.ChoiceCase);
            Assert.Equal(original.J, restored.J);
        }
        finally
        {
            Descriptor.ClearRemaps();
        }
    }

    [Fact]
    public async Task Remap_IsThreadSafe_UnderConcurrentSerializationAndWrites()
    {
        // Hammers the three races the descriptor must survive: a half-built index
        // observed by a deserializing reader, two writers racing on the shared index,
        // and the fast-path gate read torn from the table it guards. The copy-on-write
        // snapshot must let every reader see one complete, self-consistent state.
        const int duration = 750; // ms
        var failures = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        using var stop = new CancellationTokenSource(duration);
        var token = stop.Token;

        try
        {
            var serialize = () => {
                var message = new RemapProbe { A = 123 };

                while (!token.IsCancellationRequested)
                {
                    var bytes = message.ToByteArray(Serializer);
                    // The 'a' field is the only one set, so the wire must carry one
                    // complete snapshot: number 21 (default) or 99 (remapped), value 123.
                    using var input = new CodedInputStream(bytes);
                    var number = WireFormat.GetTagFieldNumber(input.ReadTag());
                    Assert.True(number is 21 or 99, $"torn field number {number}");
                    Assert.Equal(expected: 123, input.ReadInt32());
                }
            };

            var lookup = () => {
                while (!token.IsCancellationRequested)
                {
                    // FindByNumber races the index swap; it must never throw or
                    // observe a half-cleared dictionary, only resolve or miss cleanly.
                    _ = Descriptor.FindByNumber(21);
                    _ = Descriptor.FindByNumber(99);
                }
            };

            var remap = () => {
                while (!token.IsCancellationRequested)
                {
                    Descriptor.Remap("a", wireNumber: 99);
                    Descriptor.ClearRemaps();
                }
            };

            var work = new[] { serialize, serialize, lookup, lookup, remap, remap };

            var tasks = work.Select(w => Task.Run(() => {
                try
                {
                    w();
                }
                catch (Exception e)
                {
                    failures.Enqueue(e);
                }
            })).ToArray();

            await Task.WhenAll(tasks);
        }
        finally
        {
            Descriptor.ClearRemaps();
        }

        Assert.True(failures.IsEmpty, failures.FirstOrDefault()?.ToString());
    }

    [Fact]
    public void ReflectivePath_RoundTrips_MessageOneofCase()
    {
        var original = new RemapProbe { K = new RemapSub { N = 77 } };

        try
        {
            Descriptor.Remap("k", Descriptor.Find("k")!.DefaultNumber);

            var restored = new RemapProbe();
            restored.MergeFrom(Serializer, original.ToByteArray(Serializer));

            Assert.Equal(RemapProbe.ChoiceOneofCase.K, restored.ChoiceCase);
            Assert.Equal(expected: 77, restored.K!.N);
        }
        finally
        {
            Descriptor.ClearRemaps();
        }
    }
}
