using System.Security.Cryptography;

namespace Starlight.Crypto;

/// <summary>
/// Shared helpers for reading RSA key material from disk or base64 strings,
/// transparently accepting both DER bytes and PEM text.
/// </summary>
internal static class RsaKeyLoader
{
    private const string PrivatePemBegin = "-----BEGIN PRIVATE KEY-----";
    private const string PrivatePemEnd = "-----END PRIVATE KEY-----";
    private const string PemBeginPrefix = "-----BEGIN";
    private const string Pkcs1PrivatePemBegin = "-----BEGIN RSA PRIVATE KEY-----";
    private const string EncryptedPrivatePemBegin = "-----BEGIN ENCRYPTED PRIVATE KEY-----";

    /// <summary>Reads PKCS#8 private-key bytes from a file (DER or PEM).</summary>
    public static byte[] ReadPkcs8File(string path)
    {
        var contents = File.ReadAllText(path);

        return contents.Contains(PrivatePemBegin, StringComparison.Ordinal) ?
            Convert.FromBase64String(StripPem(contents, PrivatePemBegin, PrivatePemEnd)) :
            File.ReadAllBytes(path);
    }

    /// <summary>
    /// Loads an RSA private key from a file, accepting any PEM-encoded key
    /// (PKCS#1 "RSA PRIVATE KEY", PKCS#8 "PRIVATE KEY") as well as raw PKCS#8 DER.
    /// </summary>
    public static RSA LoadPrivateKeyFile(string path)
    {
        var rsa = RSA.Create();
        var contents = File.ReadAllText(path);

        if (contents.Contains(PemBeginPrefix, StringComparison.Ordinal))
        {
            if (!contents.Contains(PrivatePemBegin, StringComparison.Ordinal)
                && !contents.Contains(Pkcs1PrivatePemBegin, StringComparison.Ordinal)
                && !contents.Contains(EncryptedPrivatePemBegin, StringComparison.Ordinal))
            {
                rsa.Dispose();

                throw new ArgumentException(
                    $"'{path}' is not a private-key PEM (expected PKCS#8, PKCS#1, or encrypted private key).",
                    nameof(path));
            }

            rsa.ImportFromPem(contents);
        } else
        {
            rsa.ImportPkcs8PrivateKey(File.ReadAllBytes(path), out _);
        }

        return rsa;
    }

    /// <summary>Decodes a base64 PKCS#8 private key, with or without PEM markers.</summary>
    public static byte[] DecodePkcs8(string base64)
        => Convert.FromBase64String(StripPem(base64, PrivatePemBegin, PrivatePemEnd));

    private static string StripPem(string contents, string begin, string end) => contents
        .Replace(begin, string.Empty)
        .Replace(end, string.Empty)
        .Replace("\r", string.Empty)
        .Replace("\n", string.Empty)
        .Trim();
}
