namespace Starlight.Kcp.Internals;

public sealed class ByteBuffer
{
    private readonly byte[] _bytes;

    public ByteBuffer(byte[] bytes)
    {
        _bytes = bytes;
    }

    public int BytesWritten { get; private set; }

    public bool IsEmpty => BytesWritten == 0;
    public int Remaining => _bytes.Length - BytesWritten;

    public byte[] GetWrittenBytes()
    {
        var result = new byte[BytesWritten];
        Array.Copy(_bytes, sourceIndex: 0, result, destinationIndex: 0, BytesWritten);
        return result;
    }

    public void SetBytesRead(int value)
    {
        if (value < 0 || value > _bytes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        BytesWritten = value;
    }

    public void Clear() => BytesWritten = 0;

    public void Write(byte[] data)
    {
        EnsureCapacity(data.Length);
        Array.Copy(data, sourceIndex: 0, _bytes, BytesWritten, data.Length);
        BytesWritten += data.Length;
    }

    public void Write8(byte data)
    {
        EnsureCapacity(1);
        _bytes[BytesWritten++] = data;
    }

    public void Write16LE(int data)
    {
        EnsureCapacity(2);
        _bytes[BytesWritten++] = unchecked((byte)(data & 0xFF));
        _bytes[BytesWritten++] = unchecked((byte)(data >> 8 & 0xFF));
    }

    public void Write32LE(int data)
    {
        EnsureCapacity(4);

        for (var i = 0; i < 4; i++)
        {
            _bytes[BytesWritten++] = unchecked((byte)(data >> i * 8 & 0xFF));
        }
    }

    public void Write32LE(uint data)
    {
        EnsureCapacity(4);

        for (var i = 0; i < 4; i++)
        {
            _bytes[BytesWritten++] = (byte)(data >> i * 8 & 0xFF);
        }
    }

    public void Write64LE(long data)
    {
        EnsureCapacity(8);

        for (var i = 0; i < 8; i++)
        {
            _bytes[BytesWritten++] = unchecked((byte)(data >> i * 8 & 0xFF));
        }
    }

    public void Write64BE(long data)
    {
        EnsureCapacity(8);

        for (var i = 7; i >= 0; i--)
        {
            _bytes[BytesWritten++] = unchecked((byte)(data >> i * 8 & 0xFF));
        }
    }

    public int Read16BE()
    {
        EnsureReadable(2);
        var result = _bytes[BytesWritten] << 8 | _bytes[BytesWritten + 1];
        BytesWritten += 2;
        return result;
    }

    public int Read32BE()
    {
        EnsureReadable(4);

        var result = _bytes[BytesWritten] << 24
                     | _bytes[BytesWritten + 1] << 16
                     | _bytes[BytesWritten + 2] << 8
                     | _bytes[BytesWritten + 3];
        BytesWritten += 4;
        return result;
    }

    public void Skip(int bytes)
    {
        EnsureCapacity(bytes);
        BytesWritten += bytes;
    }

    private void EnsureCapacity(int count)
    {
        if (count < 0 || Remaining < count)
        {
            throw new InvalidOperationException($"ByteBuffer cannot write/skip {count} byte(s); only {Remaining} remain.");
        }
    }

    private void EnsureReadable(int count)
    {
        if (count < 0 || Remaining < count)
        {
            throw new InvalidOperationException($"ByteBuffer cannot read {count} byte(s); only {Remaining} remain.");
        }
    }
}
