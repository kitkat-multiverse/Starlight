using Google.Protobuf;
using Starlight.Protobuf.Inspection;
using Starlight.Protobuf.Reflection;
using Starlight.Protobuf.Serialization;
using Xunit;

namespace Starlight.Protobuf.Tests;

/// <summary>
/// Reflection registry: <c>.proto</c> text parsed at runtime into
/// <see cref="DynamicMessage"/>es (de)serialized through the shared
/// <see cref="ReflectiveEngine"/>. Proves byte-parity against a Google.Protobuf
/// oracle, lossless round-trip across every kind, <c>// CmdId:</c>-driven version
/// detection, first-stop/fall-through semantics, and JSON inspection.
/// </summary>
public sealed class ReflectionRegistryTests
{
    private const string Schema = """
                                  syntax = "proto3";
                                  package v99;

                                  enum Color { RED = 0; GREEN = 1; BLUE = 2; }

                                  message Sub { int32 n = 1; }

                                  // CmdId: 4001
                                  message Probe {
                                    int32 a = 1;
                                    string b = 2;
                                    optional int32 c = 3;
                                    repeated int32 d = 4;
                                    map<int32, string> e = 5;
                                    Sub f = 6;
                                    repeated Sub g = 7;
                                    Color color = 8;
                                    oneof choice {
                                      int32 h = 9;
                                      Sub i = 10;
                                    }
                                  }

                                  // CmdId: 100
                                  message GetPlayerTokenReq { int32 uid = 1; }

                                  message ScalarBag {
                                    int32 f_int32 = 1;
                                    int64 f_int64 = 2;
                                    uint32 f_uint32 = 3;
                                    uint64 f_uint64 = 4;
                                    sint32 f_sint32 = 5;
                                    sint64 f_sint64 = 6;
                                    fixed32 f_fixed32 = 7;
                                    fixed64 f_fixed64 = 8;
                                    sfixed32 f_sfixed32 = 9;
                                    sfixed64 f_sfixed64 = 10;
                                    bool f_bool = 11;
                                    float f_float = 12;
                                    double f_double = 13;
                                    string f_string = 14;
                                    bytes f_bytes = 15;
                                  }
                                  """;

    private static ReflectionRegistry Load() =>
        ReflectionRegistry.Load(new Dictionary<string, string> { ["schema.proto"] = Schema });

    [Fact]
    public void Version_DerivedFromPackage()
    {
        Assert.Equal("V99", Load().Version);
    }

    [Fact]
    public void KnownFirst_PopulatedFromCmdIdComments()
    {
        var registry = Load();
        Assert.Contains(expected: 100, registry.KnownFirst); // GetPlayerTokenReq
        Assert.DoesNotContain(expected: 4001, registry.KnownFirst); // Probe is not a first-packet name
    }

    [Fact]
    public void FirstStop_KnowsHitsAndMisses()
    {
        var registry = Load();
        Assert.True(registry.Knows(4001));
        Assert.False(registry.Knows(9999));
        // Fall-through signal: an unknown CmdId is not constructible here.
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.Create(9999));
    }

    [Fact]
    public void ScalarBag_MatchesGoogleProtobufBytes()
    {
        var registry = Load();
        var message = registry.CreateByName("ScalarBag");
        message.Set("f_int32", value: 123);
        message.Set("f_int64", value: -456L);
        message.Set("f_uint32", value: 789u);
        message.Set("f_uint64", value: 1011UL);
        message.Set("f_sint32", value: -1213);
        message.Set("f_sint64", value: -1415L);
        message.Set("f_fixed32", value: 1617u);
        message.Set("f_fixed64", value: 1819UL);
        message.Set("f_sfixed32", value: -1920);
        message.Set("f_sfixed64", value: -2122L);
        message.Set("f_bool", value: true);
        message.Set("f_float", value: 3.14f);
        message.Set("f_double", value: 2.71828);
        message.Set("f_string", "matrix");
        message.Set("f_bytes", ByteString.CopyFromUtf8("raw"));

        var bytes = registry.Serialize(message);

        using var stream = new MemoryStream();
        using var o = new CodedOutputStream(stream);
        o.WriteTag(fieldNumber: 1, WireFormat.WireType.Varint);
        o.WriteInt32(123);
        o.WriteTag(fieldNumber: 2, WireFormat.WireType.Varint);
        o.WriteInt64(-456);
        o.WriteTag(fieldNumber: 3, WireFormat.WireType.Varint);
        o.WriteUInt32(789);
        o.WriteTag(fieldNumber: 4, WireFormat.WireType.Varint);
        o.WriteUInt64(1011);
        o.WriteTag(fieldNumber: 5, WireFormat.WireType.Varint);
        o.WriteSInt32(-1213);
        o.WriteTag(fieldNumber: 6, WireFormat.WireType.Varint);
        o.WriteSInt64(-1415);
        o.WriteTag(fieldNumber: 7, WireFormat.WireType.Fixed32);
        o.WriteFixed32(1617);
        o.WriteTag(fieldNumber: 8, WireFormat.WireType.Fixed64);
        o.WriteFixed64(1819);
        o.WriteTag(fieldNumber: 9, WireFormat.WireType.Fixed32);
        o.WriteSFixed32(-1920);
        o.WriteTag(fieldNumber: 10, WireFormat.WireType.Fixed64);
        o.WriteSFixed64(-2122);
        o.WriteTag(fieldNumber: 11, WireFormat.WireType.Varint);
        o.WriteBool(true);
        o.WriteTag(fieldNumber: 12, WireFormat.WireType.Fixed32);
        o.WriteFloat(3.14f);
        o.WriteTag(fieldNumber: 13, WireFormat.WireType.Fixed64);
        o.WriteDouble(2.71828);
        o.WriteTag(fieldNumber: 14, WireFormat.WireType.LengthDelimited);
        o.WriteString("matrix");
        o.WriteTag(fieldNumber: 15, WireFormat.WireType.LengthDelimited);
        o.WriteBytes(ByteString.CopyFromUtf8("raw"));
        o.Flush();

        Assert.Equal(stream.ToArray(), bytes);
    }

    [Fact]
    public void Probe_RoundTrips_AcrossEveryKind()
    {
        var registry = Load();
        var original = (DynamicMessage)registry.Create(4001); // Probe via CmdId
        original.Set("a", value: 42);
        original.Set("b", "hello");
        original.Set("c", value: -7);
        original.GetList("d").Add(1);
        original.GetList("d").Add(2);
        original.GetList("d").Add(300);
        original.GetMap("e")[10] = "ten";
        original.GetMap("e")[20] = "twenty";

        var sub = registry.CreateByName("Sub");
        sub.Set("n", value: 99);
        original.Set("f", sub);

        var g1 = registry.CreateByName("Sub");
        g1.Set("n", value: 1);
        var g2 = registry.CreateByName("Sub");
        g2.Set("n", value: 2);
        original.GetList("g").Add(g1);
        original.GetList("g").Add(g2);

        original.Set("color", value: 2); // BLUE
        original.SetOneof("Choice", caseNumber: 9, value: 4567); // oneof field h (base number 9)

        var bytes = registry.Serialize(original);
        var restored = (DynamicMessage)registry.Create(4001);
        using var stream = new CodedInputStream(bytes);
        registry.Deserialize(restored, stream);

        Assert.Equal(expected: 42, restored.Get("a"));
        Assert.Equal("hello", restored.Get("b"));
        Assert.Equal(expected: -7, restored.Get("c"));
        Assert.Equal([1, 2, 300], restored.GetList("d").Cast<object?>());
        Assert.Equal("ten", restored.GetMap("e")[10]);
        Assert.Equal("twenty", restored.GetMap("e")[20]);
        Assert.Equal(expected: 99, ((DynamicMessage)restored.Get("f")!).Get("n"));
        var gs = restored.GetList("g").Cast<DynamicMessage>().Select(m => m.Get("n")).ToArray();
        Assert.Equal([1, 2], gs);
        Assert.Equal(expected: 2, restored.Get("color"));
        Assert.Equal(expected: 9, restored.ActiveOneof("Choice"));
        Assert.Equal(expected: 4567, restored.GetOneof("Choice"));
    }

    [Fact]
    public void Probe_RoundTrips_MessageOneofCase()
    {
        var registry = Load();
        var original = registry.CreateByName("Probe");
        var sub = registry.CreateByName("Sub");
        sub.Set("n", value: 77);
        original.SetOneof("Choice", caseNumber: 10, sub); // oneof field i (base number 10)

        var bytes = registry.Serialize(original);
        var restored = registry.CreateByName("Probe");
        using var stream = new CodedInputStream(bytes);
        registry.Deserialize(restored, stream);

        Assert.Equal(expected: 10, restored.ActiveOneof("Choice"));
        Assert.Equal(expected: 77, ((DynamicMessage)restored.GetOneof("Choice")!).Get("n"));
    }

    [Fact]
    public void Deserialize_CapturesUnknownFields()
    {
        var registry = Load();

        // A wire payload with a field (50) the schema does not define.
        using var stream = new MemoryStream();
        using var cos = new CodedOutputStream(stream);
        cos.WriteTag(fieldNumber: 1, WireFormat.WireType.Varint);
        cos.WriteInt32(5); // Sub.n
        cos.WriteTag(fieldNumber: 50, WireFormat.WireType.Varint);
        cos.WriteInt32(999); // unknown
        cos.Flush();

        var message = registry.CreateByName("Sub");
        using var cis = new CodedInputStream(stream.ToArray());
        registry.Deserialize(message, cis);

        Assert.Equal(expected: 5, message.Get("n"));
        Assert.NotNull(message.UnknownFields);
        Assert.Contains(message.UnknownFields!.Fields, fld => fld.FieldNumber == 50);
    }

    [Fact]
    public void Inspect_RendersDynamicFields()
    {
        var registry = Load();
        var message = registry.CreateByName("Probe");
        message.Set("a", value: 7);
        message.Set("b", "hi");

        var json = ProtocolInspector.ToJson(message);

        Assert.Contains("\"a\":7", json);
        Assert.Contains("\"b\":\"hi\"", json);
    }
}
