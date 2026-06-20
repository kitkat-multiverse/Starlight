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

        if (metadataLen > data.Length || bodyLen > data.Length)
        {
            throw new PacketParseException($"Invalid metadata or body length received in game packet");
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
        var payload = new byte[sizeof(ushort) * 4 + metadata.Length + Body.Length];

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
