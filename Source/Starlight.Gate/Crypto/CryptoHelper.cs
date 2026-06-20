namespace Starlight.Gate.Crypto;

public static class CryptoHelper
{
    /// <summary>
    /// Performs a simple XOR cipher with the given data & key.
    /// </summary>
    public static void Xor(byte[] data, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length == 0)
            throw new ArgumentException("Key cannot be empty", nameof(key));

        for (var i = 0; i < data.Length; i++)
        {
            data[i] ^= key[i % key.Length];
        }
    }
}
