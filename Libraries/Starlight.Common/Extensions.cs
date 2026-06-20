using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Starlight.Common;

public static class RandomExtensions
{
    /// <summary>
    /// Generates a random version 4 UUID.
    /// </summary>
    public static Guid NextUuid(this Random random)
    {
        Span<byte> bytes = stackalloc byte[16];
        random.NextBytes(bytes);

        bytes[6] = (byte)(bytes[6] & 0x0F | 0x40); // version 4
        bytes[8] = (byte)(bytes[8] & 0x3F | 0x80); // variant 1 (RFC 4122)

        return new Guid(bytes);
    }
}

public static class StringExtensions
{
    /// <summary>
    /// Returns the file extension of a file name.
    /// </summary>
    public static string FileExtension(this string fileName)
    {
        var index = fileName.LastIndexOf('.');
        return index == -1 ? string.Empty : fileName[(index + 1)..];
    }
}

public static class SpanExtensions
{
#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type
    public static unsafe T ReadBe<T>(this ReadOnlySpan<byte> b, ref int offset)
        where T : struct
    {
        offset += sizeof(T);
        var slice = b.Slice(offset - sizeof(T), sizeof(T));
        if (!BitConverter.IsLittleEndian) return MemoryMarshal.Read<T>(slice);

        Span<byte> reversed = stackalloc byte[sizeof(T)];
        slice.CopyTo(reversed);
        reversed.Reverse();
        return MemoryMarshal.Read<T>(reversed);
    }
#pragma warning restore CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type
    public static unsafe void WriteBe<T>(this Span<byte> b, ref int offset, T value)
        where T : struct
    {
        var size = sizeof(T);

        if (!BitConverter.IsLittleEndian)
        {
            MemoryMarshal.Write(b.Slice(offset, size), in value);
        } else
        {
            Span<byte> reversed = stackalloc byte[size];
            MemoryMarshal.Write(reversed, in value);
            reversed.Reverse();
            reversed.CopyTo(b.Slice(offset, size));
        }
        offset += size;
    }
#pragma warning restore CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type
}
