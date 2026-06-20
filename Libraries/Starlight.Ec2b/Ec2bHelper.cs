using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Starlight.Ec2b;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public static class Ec2bHelper
{
    public static byte[] Derive(ReadOnlySpan<byte> ec2b)
    {
        if (ec2b.Length != 2076)
            throw new ArgumentException($"ec2b size must be 2076 (got {ec2b.Length})");

        Span<byte> key = stackalloc byte[16];
        Span<byte> data = stackalloc byte[2048];

        ec2b.Slice(start: 8, length: 16).CopyTo(key);
        ec2b.Slice(start: 28, length: 2048).CopyTo(data);

        KeyScramble(key);

        var keyX = Magic.KeyXorpadTable;
        if (keyX.Length < 16) throw new InvalidOperationException("KeyXorpadTable not initialized or too short.");

        for (var i = 0; i < 16; i++)
            key[i] ^= keyX[i];

        var xorpad = new byte[4096];
        GetDecryptVector(key, data, xorpad);

        return xorpad;
    }

    /// <summary>
    /// Derives a secret key (<see cref="Derive"/>'s <c>ec2b</c> parameter) from a seed phrase.
    /// <br/>
    /// This is a custom algorithm reused by the dispatch server & gate servers to derive the same secret.
    /// </summary>
    public static byte[] DeriveSecret(string seedPhrase)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seedPhrase));
        var seed = BinaryPrimitives.ReadUInt64LittleEndian(hash);

        var mt = new Mt19937_64(seed);
        var result = new byte[2076];

        var qwords = MemoryMarshal.Cast<byte, ulong>(result.AsSpan(start: 0, length: 2072));

        for (var i = 0; i < qwords.Length; i++)
            qwords[i] = mt.NextULong();

        Span<byte> tail = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(tail, mt.NextULong());
        tail[..4].CopyTo(result.AsSpan(2072));

        return result;
    }

    private static void KeyScramble(Span<byte> key)
    {
        Span<byte> roundKeys = stackalloc byte[16 * 11];
        var t0 = Magic.AesXorpadTable[0];
        var t1 = Magic.AesXorpadTable[1];

        for (var round = 0; round <= 10; round++)
        {
            var roundBase = round * 16;
            var idxBase = round << 8;

            for (var i = 0; i < 16; i++)
            {
                var idxBaseRow = idxBase + i * 16;
                byte acc = 0;

                for (var j = 0; j < 16; j++)
                {
                    var idx = idxBaseRow + j;
                    acc ^= (byte)(t1[idx] ^ t0[idx]);
                }
                roundKeys[roundBase + i] ^= acc;
            }
        }

        Span<byte> chip = stackalloc byte[16];
        AesMhy.EncryptMhy(key, roundKeys, chip);
        chip.CopyTo(key);
    }

    private static void GetDecryptVector(ReadOnlySpan<byte> key, ReadOnlySpan<byte> crypt, Span<byte> output)
    {
        var val = 0xFFFFFFFFFFFFFFFFUL;

        var qwords = MemoryMarshal.Cast<byte, ulong>(crypt[..^(crypt.Length % 8)]);

        foreach (var t in qwords)
            val ^= t;

        if (key.Length < 16) throw new ArgumentException("key must be 16 bytes");

        var k0 = BinaryPrimitives.ReadUInt64LittleEndian(key[..8]);
        var k1 = BinaryPrimitives.ReadUInt64LittleEndian(key[8..16]);
        var seed = k1 ^ 0xCEAC3B5A867837ACUL ^ val ^ k0;

        var mt = new Mt19937_64(seed);

        // Fill output with 64-bit mt() values
        var outQ = MemoryMarshal.Cast<byte, ulong>(output[..^(output.Length % 8)]);

        for (var i = 0; i < outQ.Length; i++)
            outQ[i] = mt.NextULong();
    }
}
