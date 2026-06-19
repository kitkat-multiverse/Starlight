using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Starlight.Ec2b;

public static class Ec2b
{
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

        var qwords = MemoryMarshal.Cast<byte, ulong>(crypt.Slice(start: 0, crypt.Length - crypt.Length % 8));

        for (var i = 0; i < qwords.Length; i++)
            val ^= qwords[i];

        if (key.Length < 16) throw new ArgumentException("key must be 16 bytes");

        var k0 = BinaryPrimitives.ReadUInt64LittleEndian(key.Slice(start: 0, length: 8));
        var k1 = BinaryPrimitives.ReadUInt64LittleEndian(key.Slice(start: 8, length: 8));
        var seed = k1 ^ 0xCEAC3B5A867837ACUL ^ val ^ k0;

        var mt = new Mt19937_64(seed);

        // Fill output with 64-bit mt() values
        var outQ = MemoryMarshal.Cast<byte, ulong>(output.Slice(start: 0, output.Length - output.Length % 8));

        for (var i = 0; i < outQ.Length; i++)
            outQ[i] = mt.NextULong();
    }

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
}
