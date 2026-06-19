using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Mt19937_64 = Starlight.Ec2b.Mt19937_64;

namespace Starlight.Gate.Crypto;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public static class Ec2bHelper
{
    /// <summary>
    /// Derives the secret key used in <see cref="Ec2b"/> & sent to the client in <code>client_secret_key</code>.
    /// </summary>
    /// <param name="regionId">The ID of the region specified.</param>
    /// <returns>The 2076 bytes from the region identifier.</returns>
    public static byte[] DeriveSecret(string regionId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(regionId));
        var seed = BinaryPrimitives.ReadUInt64LittleEndian(hash);

        var mt = new Mt19937_64(seed);
        var result = new byte[2076];

        var qwords = MemoryMarshal.Cast<byte, ulong>(result.AsSpan(0, 2072));
        for (var i = 0; i < qwords.Length; i++)
            qwords[i] = mt.NextULong();

        Span<byte> tail = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(tail, mt.NextULong());
        tail[..4].CopyTo(result.AsSpan(2072));

        return result;
    }
}
