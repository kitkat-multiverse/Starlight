using System.Text;

namespace Starlight.Kcp.Internals;

public sealed class ByteCursor
{
    private readonly byte[] _bytes;
    private readonly int _start;
    private int _location;

    public ByteCursor(byte[] bytes, int start = 0, int? size = null)
    {
        _bytes = bytes;
        _start = start;
        _location = start;
        Size = size ?? bytes.Length;

        if (start < 0 || Size < 0 || start + Size > bytes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Invalid cursor start/size for byte array.");
        }
    }

    public int Size { get; }
    public int BytesRead => _location - _start;
    public int Remaining => Size - BytesRead;

    public void SetBytesRead(int value)
    {
        if (value < 0 || value > Size)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        _location = _start + value;
    }

    public ByteCursor ReadCursor(int bytesToRead)
    {
        EnsureAvailable(bytesToRead);
        var result = new ByteCursor(_bytes, _location, bytesToRead);
        _location += bytesToRead;
        return result;
    }

    public byte[] Read(int bytesToRead)
    {
        EnsureAvailable(bytesToRead);
        var result = new byte[bytesToRead];
        Array.Copy(_bytes, _location, result, destinationIndex: 0, bytesToRead);
        _location += bytesToRead;
        return result;
    }

    public byte Read8()
    {
        EnsureAvailable(1);
        return _bytes[_location++];
    }

    public byte Read8U() => Read8();

    public int Read16LE()
    {
        EnsureAvailable(2);

        var value = _bytes[_location]
                    | _bytes[_location + 1] << 8;
        _location += 2;
        return value;
    }

    public int Read32LE()
    {
        EnsureAvailable(4);

        var value = _bytes[_location]
                    | _bytes[_location + 1] << 8
                    | _bytes[_location + 2] << 16
                    | _bytes[_location + 3] << 24;
        _location += 4;
        return value;
    }

    public long Read64LE()
    {
        EnsureAvailable(8);
        ulong value = 0;

        for (var i = 0; i < 8; i++)
        {
            value |= (ulong)_bytes[_location + i] << i * 8;
        }

        _location += 8;
        return unchecked((long)value);
    }

    public int Peek32LE(int skipBytes)
    {
        if (skipBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(skipBytes));
        }

        EnsureAvailable(skipBytes + 4);
        var index = _location + skipBytes;

        return _bytes[index]
               | _bytes[index + 1] << 8
               | _bytes[index + 2] << 16
               | _bytes[index + 3] << 24;
    }

    public int Read16BE()
    {
        EnsureAvailable(2);

        var value = _bytes[_location] << 8
                    | _bytes[_location + 1];
        _location += 2;
        return value;
    }

    public int Read32BE()
    {
        EnsureAvailable(4);

        var value = _bytes[_location] << 24
                    | _bytes[_location + 1] << 16
                    | _bytes[_location + 2] << 8
                    | _bytes[_location + 3];
        _location += 4;
        return value;
    }

    public long Read64BE()
    {
        EnsureAvailable(8);
        ulong value = 0;

        for (var i = 0; i < 8; i++)
        {
            value = value << 8 | _bytes[_location + i];
        }

        _location += 8;
        return unchecked((long)value);
    }

    public string ReadString(int size)
    {
        EnsureAvailable(size);
        var result = Encoding.UTF8.GetString(_bytes, _location, size);
        _location += size;
        return result;
    }

    public void Skip(int bytes)
    {
        EnsureAvailable(bytes);
        _location += bytes;
    }

    public byte[] ToByteArray()
    {
        var result = new byte[Size];
        Array.Copy(_bytes, _start, result, destinationIndex: 0, Size);
        return result;
    }

    private void EnsureAvailable(int count)
    {
        if (count < 0 || Remaining < count)
        {
            throw new InvalidOperationException($"ByteCursor cannot read {count} byte(s); only {Remaining} remain.");
        }
    }
}
