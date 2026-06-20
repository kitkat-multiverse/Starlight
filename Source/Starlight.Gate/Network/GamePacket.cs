using Starlight.Common;
using Starlight.Game.Protocol;
using Starlight.Protobuf.Core;
using Starlight.Protobuf.Registry;

namespace Starlight.Gate.Network;

public class PacketParseException(string message) : Exception(message);

public sealed class GamePacket
{
    private const ushort Header = 0x4567, Footer = 0x89ab;

    public readonly ushort CmdId;
    public readonly PacketHead Metadata;
    public readonly byte[] Body;

    public GamePacket(ReadOnlySpan<byte> data)
    {
        var offset = 0;

        var header = data.ReadBe<ushort>(ref offset);

        if (header != Header)
        {
            throw new PacketParseException($"Invalid game packet header; got {header} but expected {Header}");
        }

        CmdId = data.ReadBe<ushort>(ref offset);
        var metadataLen = data.ReadBe<ushort>(ref offset);
        var bodyLen = data.ReadBe<uint>(ref offset);

        // Header(2) + CmdId(2) + metadataLen(2) + bodyLen(4) = 10 already read bytes
        var remaining = data.Length - 10;
        var needed = metadataLen + bodyLen + sizeof(ushort);

        if (remaining != needed)
        {
            throw new PacketParseException("Invalid game packet length; expected " + needed + " bytes but got " + remaining);
        }

        Metadata = new PacketHead();
        Metadata.MergeFrom(data.Slice(offset, metadataLen).ToArray());
        offset += metadataLen;

        var body = data.Slice(offset, (int)bodyLen);
        Body = body.ToArray();
        offset += (int)bodyLen;

        var footer = data.ReadBe<ushort>(ref offset);

        if (footer != Footer)
        {
            throw new PacketParseException($"Invalid game packet footer; got {footer} but expected {Footer}");
        }
    }

    public GamePacket(ProtocolRegistry registry, IMessage message, PacketHead? metadata = null)
    {
        CmdId = (ushort)registry.GetCmdId(message);
        Metadata = metadata ?? new PacketHead();
        Body = registry.Serialize(message);
    }

    public byte[] ToBytes()
    {
        var metadata = Metadata.ToByteArray();

        var offset = 0;
        // Header(2) + CmdId(2) + metadataLen(2) + bodyLen(4) + Footer(2) = 12 fixed bytes
        var payload = new byte[12 + metadata.Length + Body.Length];

        payload.WriteBe(ref offset, Header);
        payload.WriteBe(ref offset, CmdId);
        payload.WriteBe(ref offset, (ushort)metadata.Length);
        payload.WriteBe(ref offset, (uint)Body.Length);

        Array.Copy(metadata, sourceIndex: 0, payload, offset, metadata.Length);
        offset += metadata.Length;
        Array.Copy(Body, sourceIndex: 0, payload, offset, Body.Length);
        offset += Body.Length;

        payload.WriteBe(ref offset, Footer);

        return payload;
    }
}
