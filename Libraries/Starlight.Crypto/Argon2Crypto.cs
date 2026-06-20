using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Starlight.Crypto;

/// <summary>
/// A cryptography helper specifically for the `Argon2` algorithm.
/// <br/>
/// The difference between this and a regular algorithm is that it is
/// specifically designed for <b>password hashing</b>.
/// </summary>
public static class Argon2Crypto
{
    private const int DegreeOfParallelism = 1;
    private const int MemorySizeKb = 19456; // 19 MiB
    private const int Iterations = 2;
    private const int SaltSize = 16; // 128-bit salt
    private const int HashSize = 32; // 256-bit output hash

    private static string ToHashString(byte[] hash, byte[] salt) => $"argon2id${Convert.ToBase64String(hash)}${Convert.ToBase64String(salt)}";

    /// <summary>
    /// Decodes the given hash string given the above format.
    /// </summary>
    /// <param name="hash">A hash formatted as <code>argon2id$[hash]$[salt]</code></param>
    /// <exception cref="InvalidDataException">If the hash does not meet the expected format.</exception>
    /// <returns>The (hash, salt) as bytes.</returns>
    private static (byte[], byte[]) FromHashString(string hash)
    {
        var parts = hash.Split('$');

        if (parts.Length != 3 || !string.Equals(parts[0], "argon2id", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Invalid hash format. Expected: argon2id$[hash]$[salt].");
        }

        try
        {
            var decodedHash = Convert.FromBase64String(parts[1]);
            var decodedSalt = Convert.FromBase64String(parts[2]);

            if (decodedHash.Length != HashSize || decodedSalt.Length != SaltSize)
            {
                throw new InvalidDataException("Invalid hash or salt length.");
            }

            return (decodedHash, decodedSalt);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("Invalid hash format. Hash and salt must be Base64.", ex);
        }
    }

    public static string Hash(string content)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);

        using var hasher = new Argon2id(Encoding.UTF8.GetBytes(content)) {
            Salt = salt,
            DegreeOfParallelism = DegreeOfParallelism,
            MemorySize = MemorySizeKb,
            Iterations = Iterations
        };

        var hash = hasher.GetBytes(HashSize);
        return ToHashString(hash, salt);
    }

    public static bool Verify(string content, string expected)
    {
        (byte[] expectedHash, byte[] salt) parsed;

        try
        {
            parsed = FromHashString(expected);
        }
        catch (InvalidDataException)
        {
            return false;
        }

        using var hasher = new Argon2id(Encoding.UTF8.GetBytes(content)) {
            Salt = parsed.salt,
            DegreeOfParallelism = DegreeOfParallelism,
            MemorySize = MemorySizeKb,
            Iterations = Iterations
        };

        var actualHash = hasher.GetBytes(HashSize);
        return CryptographicOperations.FixedTimeEquals(actualHash, parsed.expectedHash);
    }
}
