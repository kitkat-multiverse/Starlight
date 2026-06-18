using Google.Protobuf;
using Starlight.Protobuf.Fixtures;
using Starlight.Protobuf.Fixtures.V99;
using Starlight.Protobuf.Inspection;
using Xunit;

namespace Starlight.Protobuf.Tests;

/// <summary>
/// Validates the generated fast path against synthetic fixtures (no live
/// protocol): per-version serializers and the <see cref="V99ProtocolRegistry"/>
/// dispatcher. Confirms emitted bytes use the version dump's field numbers (not
/// the canonical base structural ones), round-trip cleanly, capture unknown
/// version fields, and that the registry metadata is correct.
/// </summary>
public sealed class RegistrySerializerTests
{
    private static readonly V99ProtocolRegistry Registry = new();

    // Cmd ids are a per-version concept and live on the registry. The fixture
    // protos carry `// CmdId:` comments so the generated registry surfaces them.
    private static readonly int PingReqCmdId = Registry.GetCmdId(new PingReq());

    [Fact]
    public void Serialize_UsesVersionFieldNumbers_NotBaseStructuralOnes()
    {
        // In v99, PingReq.seq is wire field 4 (the base structural field is 2)
        // -> tag (4<<3)|0 = 0x20.
        var message = new PingReq { Seq = 150 };

        var bytes = Registry.Serialize(message);

        byte[] expected = [0x20, 0x96, 0x01]; // tag 0x20, value 150 (varint 0x96 0x01)
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void Serialize_OmitsProto3DefaultValues()
    {
        var bytes = Registry.Serialize(new PingReq());
        Assert.Empty(bytes);
    }

    [Fact]
    public void RoundTrip_PackedRepeatedUint32()
    {
        // tags is v99 wire field 6 (base 3) -> length-delimited
        // tag (6<<3)|2 = 0x32. proto3 packs repeated numeric scalars by default.
        var original = new PingReq { Tags = { 1, 2, 3 } };

        var bytes = Registry.Serialize(original);

        byte[] expected = [0x32, 0x03, 0x01, 0x02, 0x03]; // tag, length 3, three 1-byte varints
        Assert.Equal(expected, bytes);

        using var stream = new CodedInputStream(bytes);
        var restored = (PingReq)Registry.Deserialize(PingReqCmdId, stream);
        Assert.Equal(new uint[] { 1, 2, 3 }, restored.Tags);
    }

    [Fact]
    public void RoundTrip_PingReq_PreservesCanonicalFields()
    {
        var original = new PingReq {
            ClientId = "client-123",
            Seq = 42,
            Tags = { 7, 8, 9 },
            Flag = true
        };

        var bytes = Registry.Serialize(original);
        using var stream = new CodedInputStream(bytes);
        var restored = (PingReq)Registry.Deserialize(PingReqCmdId, stream);

        Assert.Equal(original.ClientId, restored.ClientId);
        Assert.Equal(original.Seq, restored.Seq);
        Assert.Equal(original.Tags, restored.Tags);
        Assert.Equal(original.Flag, restored.Flag);
    }

    [Fact]
    public void Deserialize_CapturesUnknownVersionFields()
    {
        // Field 1824 has no canonical counterpart, so on read it must be captured
        // (never discarded) while the known field still deserializes. client_id is
        // v99 wire field 5.
        using var output = new MemoryStream();
        using var cos = new CodedOutputStream(output);
        cos.WriteTag(fieldNumber: 1824, WireFormat.WireType.LengthDelimited);
        cos.WriteString("obfuscated");
        cos.WriteTag(fieldNumber: 5, WireFormat.WireType.LengthDelimited); // client_id
        cos.WriteString("client");
        cos.Flush();

        using var cis = new CodedInputStream(output.ToArray());
        var restored = (PingReq)Registry.Deserialize(PingReqCmdId, cis);

        Assert.Equal("client", restored.ClientId);

        Assert.NotNull(restored.UnknownFields);
        var unknown = Assert.Single(restored.UnknownFields!.Fields);
        Assert.Equal(expected: 1824, unknown.FieldNumber);
        Assert.Equal(WireFormat.WireType.LengthDelimited, unknown.WireType);
        Assert.Equal("obfuscated", System.Text.Encoding.UTF8.GetString(unknown.Data));
    }

    [Fact]
    public void Inspect_RendersKnownAndUnknownFields()
    {
        using var output = new MemoryStream();
        using var cos = new CodedOutputStream(output);
        cos.WriteTag(fieldNumber: 1824, WireFormat.WireType.LengthDelimited);
        cos.WriteString("obfuscated");
        cos.WriteTag(fieldNumber: 5, WireFormat.WireType.LengthDelimited); // client_id
        cos.WriteString("client");
        cos.Flush();

        using var cis = new CodedInputStream(output.ToArray());
        var restored = (PingReq)Registry.Deserialize(PingReqCmdId, cis);

        var json = ProtocolInspector.ToJson(restored);

        Assert.Contains("\"clientId\":\"client\"", json);
        Assert.Contains("\"_unknown\":[", json);
        Assert.Contains("\"field\":1824", json);
        Assert.Contains($"\"data\":\"{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("obfuscated"))}\"", json);
    }

    [Fact]
    public void GetCmdId_ResolvesByMessageType()
    {
        Assert.Equal(expected: 700, Registry.GetCmdId(new PingReq()));
        Assert.Equal(expected: 4242, Registry.GetCmdId(new Coverage()));
    }

    [Fact]
    public void Create_ConstructsCorrectPocoType()
    {
        Assert.IsType<PingReq>(Registry.Create(700));
        Assert.IsType<Coverage>(Registry.Create(4242));
    }

    [Fact]
    public void Registry_ExposesVersionAndKnownFirst()
    {
        Assert.Equal("V99", Registry.Version);
        Assert.Contains(expected: 700, Registry.KnownFirst);
    }
}
