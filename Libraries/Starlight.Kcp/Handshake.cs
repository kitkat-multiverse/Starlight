using System.Buffers.Binary;

namespace Starlight.Kcp;

public abstract class Handshake(uint convId = 0, uint token = 0, uint data = 1234567890)
{
    protected abstract uint Head { get; }
    protected abstract uint Foot { get; }

    public uint ConvId => convId;
    public uint Token => token;
    public uint Data => data;

    public static Handshake? Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 20) return null;

        var head = BinaryPrimitives.ReadUInt32BigEndian(payload[..4]);
        var conv = BinaryPrimitives.ReadUInt32BigEndian(payload[4..8]);
        var token = BinaryPrimitives.ReadUInt32BigEndian(payload[8..12]);
        var data = BinaryPrimitives.ReadUInt32BigEndian(payload[12..16]);
        var foot = BinaryPrimitives.ReadUInt32BigEndian(payload[16..20]);

        return (head, foot) switch {
            (0xFF, 0xFFFFFFFF) => new ConnectHandshake(),
            (0x145, 0x14514545) => new ExchangeHandshake(conv, token),
            (0x194, 0x19419494) => new DisconnectHandshake(conv, token, data),
            _ => null
        };
    }

    public byte[] ToByteArray()
    {
        Span<byte> buffer = stackalloc byte[20];
        BinaryPrimitives.WriteUInt32BigEndian(buffer[..4], Head);
        BinaryPrimitives.WriteUInt32BigEndian(buffer[4..8], ConvId);
        BinaryPrimitives.WriteUInt32BigEndian(buffer[8..12], Token);
        BinaryPrimitives.WriteUInt32BigEndian(buffer[12..16], Data);
        BinaryPrimitives.WriteUInt32BigEndian(buffer[16..20], Foot);
        return buffer.ToArray();
    }
}

public sealed class ConnectHandshake : Handshake
{
    protected override uint Head => 0xFF;
    protected override uint Foot => 0xFFFFFFFF;
}

public sealed class ExchangeHandshake(uint conv, uint token) : Handshake(conv, token)
{
    protected override uint Head => 0x145;
    protected override uint Foot => 0x14514545;
}

public sealed class DisconnectHandshake(uint conv, uint token, uint reason) : Handshake(conv, token, reason)
{
    protected override uint Head => 0x194;
    protected override uint Foot => 0x19419494;

    public DisconnectReason Reason => (DisconnectReason)Data;
}

public enum DisconnectReason : uint
{
    Timeout = 0,
    /// The client has explicitly requested to disconnect. (exit button)
    ClientClose = 1,
    ClientRebindFail = 2,
    ClientShutdown = 3,
    /// Sent when the login state of the client is invalid.
    ///
    /// Usually sent when a password reset occurs and sessions are invalidated.
    ServerRelogin = 4,
    ServerKick = 5,
    /// The server is going offline.
    ServerShutdown = 6,
    NotFoundSession = 7,
    LoginUnfinished = 8,
    PacketFreqTooHigh = 9,
    PingTimeout = 10,
    TransferFailed = 11,
    ServerKillClient = 12,
    CheckMoveSpeed = 13,
    AccountPasswordChange = 14,
    SecurityKick = 15,
    LuaShellTimeout = 16,
    SdkfailKick = 17,
    PacketCostTime = 18,
    PacketUnionFreq = 19,
    WaitSndMax = 20,
    AccountTypeBlockLogin = 21
}
