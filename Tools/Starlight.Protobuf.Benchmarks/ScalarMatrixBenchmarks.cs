using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Google.Protobuf;
using Starlight.Protobuf.Core;
using Starlight.Protobuf.Fixtures;
using Starlight.Protobuf.Fixtures.V99;
using GGoogle = Starlight.Protobuf.Benchmarks.Google;

namespace Starlight.Protobuf.Benchmarks;

/// <summary>All-scalar message: pure tag/varint/fixed encoding throughput.
/// Each op (de)serializes a batch of <see cref="N"/> messages.</summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ScalarMatrixBenchmarks
{
    private static readonly Fixtures.V99.ScalarMatrixSerializer Serializer = Fixtures.V99.ScalarMatrixSerializer.Instance;

    [Params(1, 1000)]
    public int N;

    private GGoogle.ScalarMatrix[] _google = [];
    private ScalarMatrix[] _starlight = [];
    private byte[][] _googleBytes = [];
    private byte[][] _starlightBytes = [];

    [GlobalSetup]
    public void Setup()
    {
        _google = Samples.Batch(N, Samples.GoogleScalar);
        _starlight = Samples.Batch(N, Samples.StarlightScalar);
        _googleBytes = Array.ConvertAll(_google, m => m.ToByteArray());
        _starlightBytes = Array.ConvertAll(_starlight, m => m.ToByteArray(Serializer));
    }

    [BenchmarkCategory("Serialize")]
    [Benchmark(Baseline = true)]
    public long Google_Serialize()
    {
        long total = 0;

        foreach (var message in _google)
        {
            total += message.ToByteArray().Length;
        }
        return total;
    }

    [BenchmarkCategory("Serialize")]
    [Benchmark]
    public long Starlight_Serialize()
    {
        long total = 0;

        foreach (var message in _starlight)
        {
            total += message.ToByteArray(Serializer).Length;
        }
        return total;
    }

    [BenchmarkCategory("Deserialize")]
    [Benchmark(Baseline = true)]
    public long Google_Deserialize()
    {
        long total = 0;

        foreach (var bytes in _googleBytes)
        {
            total += GGoogle.ScalarMatrix.Parser.ParseFrom(bytes).FInt32;
        }
        return total;
    }

    [BenchmarkCategory("Deserialize")]
    [Benchmark]
    public long Starlight_Deserialize()
    {
        long total = 0;

        foreach (var bytes in _starlightBytes)
        {
            var message = new ScalarMatrix();
            message.MergeFrom(Serializer, bytes);
            total += message.FInt32;
        }
        return total;
    }
}
