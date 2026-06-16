namespace Starlight.Kcp.Internals;

public sealed class ByteBuffer
{
	private readonly byte[] _bytes;
	private int _location;

	public ByteBuffer(byte[] bytes)
	{
		_bytes = bytes;
	}

	public int BytesWritten => _location;
	public bool IsEmpty => _location == 0;
	public int Remaining => _bytes.Length - _location;

	public byte[] GetWrittenBytes()
	{
		var result = new byte[_location];
		Array.Copy(_bytes, 0, result, 0, _location);
		return result;
	}

	public void SetBytesRead(int value)
	{
		if (value < 0 || value > _bytes.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(value));
		}

		_location = value;
	}

	public void Clear() => _location = 0;

	public void Write(byte[] data)
	{
		EnsureCapacity(data.Length);
		Array.Copy(data, 0, _bytes, _location, data.Length);
		_location += data.Length;
	}

	public void Write8(byte data)
	{
		EnsureCapacity(1);
		_bytes[_location++] = data;
	}

	public void Write16LE(int data)
	{
		EnsureCapacity(2);
		_bytes[_location++] = unchecked((byte)(data & 0xFF));
		_bytes[_location++] = unchecked((byte)((data >> 8) & 0xFF));
	}

	public void Write32LE(int data)
	{
		EnsureCapacity(4);
		for (var i = 0; i < 4; i++)
		{
			_bytes[_location++] = unchecked((byte)((data >> (i * 8)) & 0xFF));
		}
	}

	public void Write64LE(long data)
	{
		EnsureCapacity(8);
		for (var i = 0; i < 8; i++)
		{
			_bytes[_location++] = unchecked((byte)((data >> (i * 8)) & 0xFF));
		}
	}

	public void Write64BE(long data)
	{
		EnsureCapacity(8);
		for (var i = 7; i >= 0; i--)
		{
			_bytes[_location++] = unchecked((byte)((data >> (i * 8)) & 0xFF));
		}
	}

	public int Read16BE()
	{
		EnsureReadable(2);
		var result = (_bytes[_location] << 8) | _bytes[_location + 1];
		_location += 2;
		return result;
	}

	public int Read32BE()
	{
		EnsureReadable(4);
		var result = (_bytes[_location] << 24)
			| (_bytes[_location + 1] << 16)
			| (_bytes[_location + 2] << 8)
			| _bytes[_location + 3];
		_location += 4;
		return result;
	}

	public void Skip(int bytes)
	{
		EnsureCapacity(bytes);
		_location += bytes;
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