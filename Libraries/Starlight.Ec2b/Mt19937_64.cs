namespace Starlight.Ec2b;

public sealed class Mt19937_64
{
    private const int Nn = 312;
    private const int Mm = 156;
    private const ulong MatrixA = 0xB5026F5AA96619E9UL;
    private const ulong Um = 0xFFFFFFFF80000000UL;
    private const ulong Lm = 0x7FFFFFFFUL;

    private readonly ulong[] _mt = new ulong[Nn];
    private int _mti;

    public Mt19937_64(ulong seed)
    {
        _mt[0] = seed;

        for (_mti = 1; _mti < Nn; _mti++)
            _mt[_mti] = 6364136223846793005UL * (_mt[_mti - 1] ^ _mt[_mti - 1] >> 62) + (ulong)_mti;
    }

    public ulong NextULong()
    {
        ulong x;

        if (_mti >= Nn)
        {
            int i;

            for (i = 0; i < Nn - Mm; i++)
            {
                x = _mt[i] & Um | _mt[i + 1] & Lm;
                _mt[i] = _mt[i + Mm] ^ x >> 1 ^ (x & 1UL) * MatrixA;
            }

            for (; i < Nn - 1; i++)
            {
                x = _mt[i] & Um | _mt[i + 1] & Lm;
                _mt[i] = _mt[i + (Mm - Nn)] ^ x >> 1 ^ (x & 1UL) * MatrixA;
            }
            x = _mt[Nn - 1] & Um | _mt[0] & Lm;
            _mt[Nn - 1] = _mt[Mm - 1] ^ x >> 1 ^ (x & 1UL) * MatrixA;
            _mti = 0;
        }

        x = _mt[_mti++];

        x ^= x >> 29 & 0x5555555555555555UL;
        x ^= x << 17 & 0x71D67FFFEDA60000UL;
        x ^= x << 37 & 0xFFF7EEE000000000UL;
        x ^= x >> 43;
        return x;
    }
}
