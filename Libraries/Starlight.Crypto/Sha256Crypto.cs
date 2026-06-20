using System.Security.Cryptography;
using System.Text;

namespace Starlight.Crypto;

/// <summary>
/// SHA-256 helpers for hashing and constant-time digest comparison.
/// </summary>
public static class Sha256Crypto
{
    /// <summary>SHA-256 of <paramref name="content"/> as lowercase hex.</summary>
    public static string Hash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Constant-time equality check between a candidate plain-text password
    /// and a previously stored SHA-256 hex digest.
    /// </summary>
    public static bool Verify(string content, string expectedHash)
    {
        var computed = Hash(content);

        if (expectedHash.Length != computed.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computed),
            Encoding.ASCII.GetBytes(expectedHash));
    }
}
