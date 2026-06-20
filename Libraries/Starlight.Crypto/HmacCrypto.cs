using System.Security.Cryptography;
using System.Text;

namespace Starlight.Crypto;

/// <summary>
/// HMAC-SHA256 signature helpers used by the combo granter login flow to
/// validate that the request body has not been tampered with. The
/// hex-encoded digest must match the <c>sign</c> field on the request.
/// </summary>
public static class HmacCrypto
{
    /// <summary>
    /// Computes the HMAC-SHA256 digest of <paramref name="content"/> using
    /// <paramref name="key"/> and returns it as lowercase hex.
    /// </summary>
    public static string CreateHash(string content, string key)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var contentBytes = Encoding.UTF8.GetBytes(content);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(contentBytes);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Constant-time comparison of an expected signature against the value
    /// produced by hashing <paramref name="content"/> with <paramref name="key"/>.
    /// </summary>
    public static bool Verify(string content, string key, string expectedSignature)
    {
        var computed = CreateHash(content, key);

        if (expectedSignature.Length != computed.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computed),
            Encoding.ASCII.GetBytes(expectedSignature));
    }
}
