using System.Runtime.InteropServices;
using Starlight.Ec2b;

namespace Starlight.Gate.Crypto;

public static class MtKey
{
    private const int Length = 4096;

    /// <summary>
    /// Generates a key using <see cref="Mt19937_64"/>.
    /// </summary>
    public static byte[] Generate(ulong seed)
    {
        Span<byte> key = stackalloc byte[Length];

        var mt = new Mt19937_64(seed);
        mt.Init(mt.NextULong());
        _ = mt.NextULong();

        for (var i = 0; i < Length; i += sizeof(ulong))
        {
            var bytes = key.Slice(i, sizeof(ulong));
            MemoryMarshal.Write(bytes, mt.NextULong());
        }

        return key.ToArray();
    }
}
