using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Google.Protobuf;
using Starlight.Protobuf.Core;
using Starlight.Protobuf.Fixtures;
using Starlight.Protobuf.Fixtures.V99;
using GGoogle = Starlight.Protobuf.Benchmarks.Google;

namespace Starlight.Protobuf.Benchmarks;

/// <summary>proto3 explicit presence (optional), oneof, and a nested sub-message.
/// Each op (de)serializes a batch of <see cref="N"/> messages.</summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class CoverageBenchmarks
{
    private static readonly Fixtures.V99.CoverageSerializer Serializer = Fixtures.V99.CoverageSerializer.Instance;

    [Params(1, 1000)]
    public int N;

    private GGoogle.Coverage[] _google = [];
    private Coverage[] _starlight = [];
    private byte[][] _googleBytes = [];
    private byte[][] _starlightBytes = [];

    [GlobalSetup]
    public void Setup()
    {
        _google = Samples.Batch(N, Samples.GoogleCoverage);
        _starlight = Samples.Batch(N, Samples.StarlightCoverage);
        _googleBytes = Array.ConvertAll(_google, m => m.ToByteArray());
        _starlightBytes = Array.ConvertAll(_starlight, m => m.ToByteArray(Serializer));
    }

    [BenchmarkCategory("Serialize")] [Benchmark(Baseline = true)]
    public long Google_Serialize()
    {
        long total = 0;

        foreach (var message in _google)
        {
            total += message.ToByteArray().Length;
        }
        return total;
    }

    [BenchmarkCategory("Serialize")] [Benchmark]
    public long Starlight_Serialize()
    {
        long total = 0;

        foreach (var message in _starlight)
        {
            total += message.ToByteArray(Serializer).Length;
        }
        return total;
    }

    [BenchmarkCategory("Deserialize")] [Benchmark(Baseline = true)]
    public long Google_Deserialize()
    {
        long total = 0;

        foreach (var bytes in _googleBytes)
        {
            total += GGoogle.Coverage.Parser.ParseFrom(bytes).Plain;
        }
        return total;
    }

    [BenchmarkCategory("Deserialize")] [Benchmark]
    public long Starlight_Deserialize()
    {
        long total = 0;

        foreach (var bytes in _starlightBytes)
        {
            var message = new Coverage();
            message.MergeFrom(Serializer, bytes);
            total += message.Plain;
        }
        return total;
    }
}
