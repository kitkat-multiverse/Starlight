using BenchmarkDotNet.Running;
using Google.Protobuf;
using Starlight.Protobuf.Core;
using Starlight.Protobuf.Benchmarks;
using Starlight.Protobuf.Fixtures.V99;

// `--verify` (or no BenchmarkDotNet filter) compares the encoded output so the
// comparison can be confirmed fair before spending minutes benchmarking: both
// engines must encode each message to the exact same bytes.
if (args.Length == 1 && args[0] == "--verify")
{
    Verify("ScalarMatrix", Samples.GoogleScalar().ToByteArray(),
        Samples.StarlightScalar().ToByteArray(ScalarMatrixSerializer.Instance));

    Verify("Coverage", Samples.GoogleCoverage().ToByteArray(),
        Samples.StarlightCoverage().ToByteArray(CoverageSerializer.Instance));
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Samples).Assembly).Run(args);
return;

static void Verify(string name, byte[] google, byte[] starlight)
{
    // Equal sizes prove nothing on their own — different tags or values can land on the
    // same length, so compare the payloads themselves.
    var verdict = google.AsSpan().SequenceEqual(starlight) ? "OK (identical)" : "MISMATCH";
    Console.WriteLine($"{name,-20} google={google.Length,4}B  starlight={starlight.Length,4}B  -> {verdict}");
}
