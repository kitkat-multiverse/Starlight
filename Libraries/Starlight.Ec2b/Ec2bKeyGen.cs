using Starlight.Ec2b;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Starlight.Ec2b;

/// <summary>
/// Creates a valid 2076-byte Ec2b seed file from a caller-provided seed.
///
/// File layout produced here:
///   0x0000: ASCII "Ec2b" magic, 4 bytes
///   0x0004: little-endian uint32 0x10, key length
///   0x0008: generated key, 16 bytes
///   0x0018: little-endian uint32 0x800, data length
///   0x001C: generated data, 2048 bytes
/// </summary>
public static class Ec2bKeyGen
{
    public const int KeySize = 0x10;
    public const int DataSize = 0x800;
    public const int HeaderSize = 4 + 4 + KeySize + 4;
    public const int Ec2bSize = HeaderSize + DataSize;

    private static readonly byte[] Domain = Encoding.ASCII.GetBytes("Starlight-Ec2b");

    /// <summary>
    /// Creates a valid Ec2b buffer from arbitrary seed bytes.
    /// </summary>
    public static byte[] Create(ReadOnlySpan<byte> seed)
    {
        if (seed.IsEmpty)
            throw new ArgumentException("Seed must not be empty.", nameof(seed));

        var ec2b = new byte[Ec2bSize];
        var dst = ec2b.AsSpan();

        dst[0] = (byte)'E';
        dst[1] = (byte)'c';
        dst[2] = (byte)'2';
        dst[3] = (byte)'b';

        BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(start: 4, length: 4), KeySize);
        BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(start: 24, length: 4), DataSize);

        var generated = ExpandSeed(seed, KeySize + DataSize);
        generated.AsSpan(start: 0, KeySize).CopyTo(dst.Slice(start: 8, KeySize));
        generated.AsSpan(KeySize, DataSize).CopyTo(dst.Slice(start: 28, DataSize));

        CryptographicOperations.ZeroMemory(generated);
        return ec2b;
    }

    /// <summary>
    /// Creates a valid Ec2b buffer from UTF-8 text.
    /// For hex input, prefer CreateFromHexSeed or prefix the seed with "hex:" and call CreateFromSeedString.
    /// </summary>
    public static byte[] Create(string utf8Seed)
    {
        if (string.IsNullOrEmpty(utf8Seed))
            throw new ArgumentException("Seed must not be empty.", nameof(utf8Seed));

        return Create(Encoding.UTF8.GetBytes(utf8Seed));
    }

    /// <summary>
    /// Creates a valid Ec2b buffer from a 64-bit integer seed.
    /// </summary>
    public static byte[] Create(ulong seed)
    {
        Span<byte> seedBytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(seedBytes, seed);
        return Create(seedBytes);
    }

    /// <summary>
    /// Creates a valid Ec2b buffer from either UTF-8 text or hex prefixed with "hex:".
    /// Examples: "kazusa", "hex:DE AD BE EF", "hex:0xdeadbeef".
    /// </summary>
    public static byte[] CreateFromSeedString(string seed)
    {
        if (string.IsNullOrEmpty(seed))
            throw new ArgumentException("Seed must not be empty.", nameof(seed));

        if (seed.StartsWith("hex:", StringComparison.OrdinalIgnoreCase))
            return Create(ParseHex(seed.Substring(4)));

        return Create(seed);
    }

    /// <summary>
    /// Creates a valid Ec2b buffer from hex bytes.
    /// Accepts whitespace, underscores, dashes, and an optional leading 0x.
    /// </summary>
    public static byte[] CreateFromHexSeed(string hexSeed) => Create(ParseHex(hexSeed));

    /// <summary>
    /// Writes a valid Ec2b file from arbitrary seed bytes.
    /// </summary>
    public static void Write(ReadOnlySpan<byte> seed, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path must not be empty.", nameof(outputPath));

        File.WriteAllBytes(outputPath, Create(seed));
    }

    /// <summary>
    /// Writes a valid Ec2b file from either UTF-8 text or hex prefixed with "hex:".
    /// </summary>
    public static void WriteFromSeedString(string seed, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path must not be empty.", nameof(outputPath));

        File.WriteAllBytes(outputPath, CreateFromSeedString(seed));
    }

    /// <summary>
    /// Convenience helper: creates Ec2b and immediately derives its 4096-byte xorpad using the existing Ec2b.Derive method.
    /// </summary>
    public static (byte[] Ec2b, byte[] Xorpad) CreateWithXorpad(ReadOnlySpan<byte> seed)
    {
        var ec2b = Create(seed);
        var xorpad = Ec2b.Derive(ec2b);
        return (ec2b, xorpad);
    }

    /// <summary>
    /// Checks only the structural fields this project uses before deriving.
    /// </summary>
    public static bool HasValidLayout(ReadOnlySpan<byte> ec2b) => ec2b.Length == Ec2bSize
                                                                  && ec2b[0] == (byte)'E'
                                                                  && ec2b[1] == (byte)'c'
                                                                  && ec2b[2] == (byte)'2'
                                                                  && ec2b[3] == (byte)'b'
                                                                  && BinaryPrimitives.ReadUInt32LittleEndian(ec2b.Slice(start: 4,
                                                                      length: 4)) == KeySize
                                                                  && BinaryPrimitives.ReadUInt32LittleEndian(ec2b.Slice(start: 24,
                                                                      length: 4)) == DataSize;

    private static byte[] ExpandSeed(ReadOnlySpan<byte> seed, int outputLength)
    {
        var output = new byte[outputLength];
        var seedBytes = seed.ToArray();
        var input = new byte[Domain.Length + 4 + seedBytes.Length];

        Buffer.BlockCopy(Domain, srcOffset: 0, input, dstOffset: 0, Domain.Length);
        Buffer.BlockCopy(seedBytes, srcOffset: 0, input, Domain.Length + 4, seedBytes.Length);

        var written = 0;
        uint counter = 0;

        while (written < output.Length)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(Domain.Length, length: 4), counter++);
            var block = SHA256.HashData(input);

            var take = Math.Min(block.Length, output.Length - written);
            Buffer.BlockCopy(block, srcOffset: 0, output, written, take);
            written += take;
        }

        CryptographicOperations.ZeroMemory(seedBytes);
        CryptographicOperations.ZeroMemory(input);
        return output;
    }

    private static byte[] ParseHex(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Hex seed must not be empty.", nameof(text));

        var trimmed = text.Trim();

        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed.Substring(2);

        var clean = new StringBuilder(trimmed.Length);

        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];

            if (char.IsWhiteSpace(c) || c == '_' || c == '-')
                continue;

            if (!Uri.IsHexDigit(c))
                throw new FormatException($"Invalid hex character '{c}' at position {i}.");

            clean.Append(c);
        }

        if (clean.Length == 0 || clean.Length % 2 != 0)
            throw new FormatException("Hex seed must contain an even number of hex digits.");

        var bytes = new byte[clean.Length / 2];

        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(clean.ToString(i * 2, length: 2), fromBase: 16);

        return bytes;
    }
}
