namespace Starlight.Kcp.Internals;

public sealed class KcpSegment
{
    public KcpSegment(byte[]? data, KcpVersion version)
    {
        Data = data;
        Version = version;
    }

    public KcpVersion Version { get; }
    public byte[]? Data { get; }

    public uint Conv { get; set; }
    public uint Token { get; set; }
    public byte Cmd { get; set; }
    public byte Frg { get; set; }
    public int Wnd { get; set; }
    public int Ts { get; set; }
    public int Sn { get; set; }
    public int Una { get; set; }
    public int? ByteCheckCode { get; set; }

    // Transmission bookkeeping (not encoded on wire)
    public int ResendTs { get; set; }
    public int Rto { get; set; }
    public int FastAck { get; set; }
    public int Xmit { get; set; }

    public void Encode(ByteBuffer buf)
    {
        buf.Write32LE(Conv);
        buf.Write32LE(Token);
        buf.Write8(Cmd);
        buf.Write8(Frg);
        buf.Write16LE(Wnd);
        buf.Write32LE(Ts);
        buf.Write32LE(Sn);
        buf.Write32LE(Una);
        buf.Write32LE(Data?.Length ?? 0);

        if (Version.HasExtraHash() && ByteCheckCode.HasValue)
        {
            buf.Write32LE(ByteCheckCode.Value);
        }

        if (Data is { Length: > 0 })
        {
            buf.Write(Data);
        }
    }
}
