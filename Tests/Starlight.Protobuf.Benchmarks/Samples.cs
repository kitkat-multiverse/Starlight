using Google.Protobuf;
using GGoogle = Starlight.Protobuf.Benchmarks.Google;
using Starlight.Protobuf.Fixtures;

namespace Starlight.Protobuf.Benchmarks;

/// <summary>
/// Builds matching Google and Starlight sample messages. The two sides carry
/// identical field values and identical wire numbers, so they encode to the
/// exact same bytes — keeping each serialize/deserialize comparison fair.
/// </summary>
internal static class Samples
{
    public static GGoogle.ScalarMatrix GoogleScalar() => new() {
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

    public static ScalarMatrix StarlightScalar() => new() {
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

    public static GGoogle.Coverage GoogleCoverage() => new() {
        OptInt = 42,
        OptStr = "hello",
        Plain = 7,
        ChoiceMsg = new GGoogle.CoverageSub { Value = 99 }
    };

    public static Coverage StarlightCoverage() => new() {
        OptInt = 42,
        OptStr = "hello",
        Plain = 7,
        ChoiceMsg = new CoverageSub { Value = 99 }
    };

    /// <summary>Builds an array of <paramref name="count"/> items from a factory,
    /// the "bunch of data" a bulk benchmark serializes/deserializes per op.</summary>
    public static T[] Batch<T>(int count, Func<T> factory)
    {
        var items = new T[count];
        for (var i = 0; i < count; i++) items[i] = factory();
        return items;
    }
}
