namespace Starlight.Ec2b;

public sealed class Mt19937_64
{
	private const int NN = 312;
	private const int MM = 156;
	private const ulong MATRIX_A = 0xB5026F5AA96619E9UL;
	private const ulong UM = 0xFFFFFFFF80000000UL;
	private const ulong LM = 0x7FFFFFFFUL;

	private readonly ulong[] mt = new ulong[NN];
	private int mti;

	public Mt19937_64(ulong seed)
	{
		mt[0] = seed;
		for (mti = 1; mti < NN; mti++)
			mt[mti] = 6364136223846793005UL * (mt[mti - 1] ^ (mt[mti - 1] >> 62)) + (ulong)mti;
	}

	public ulong NextULong()
	{
		int i;
		ulong x;

		if (mti >= NN)
		{
			for (i = 0; i < NN - MM; i++)
			{
				x = (mt[i] & UM) | (mt[i + 1] & LM);
				mt[i] = mt[i + MM] ^ (x >> 1) ^ ((x & 1UL) * MATRIX_A);
			}
			for (; i < NN - 1; i++)
			{
				x = (mt[i] & UM) | (mt[i + 1] & LM);
				mt[i] = mt[i + (MM - NN)] ^ (x >> 1) ^ ((x & 1UL) * MATRIX_A);
			}
			x = (mt[NN - 1] & UM) | (mt[0] & LM);
			mt[NN - 1] = mt[MM - 1] ^ (x >> 1) ^ ((x & 1UL) * MATRIX_A);
			mti = 0;
		}

		x = mt[mti++];

		x ^= (x >> 29) & 0x5555555555555555UL;
		x ^= (x << 17) & 0x71D67FFFEDA60000UL;
		x ^= (x << 37) & 0xFFF7EEE000000000UL;
		x ^= (x >> 43);
		return x;
	}
}
