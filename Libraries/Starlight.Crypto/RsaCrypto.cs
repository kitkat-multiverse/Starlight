using System.Security.Cryptography;
using System.Text;

namespace Starlight.Crypto;

/// <summary>
/// RSA helpers.
/// </summary>
public sealed class RsaCrypto : IDisposable
{
    private readonly RSA _privateKey;

    public RsaCrypto(byte[] pkcs8PrivateKey)
    {
        _privateKey = RSA.Create();

        if (pkcs8PrivateKey.Length > 0)
        {
            _privateKey.ImportPkcs8PrivateKey(pkcs8PrivateKey, out _);
        }
    }

    private const string PemBeginMarker = "-----BEGIN PRIVATE KEY-----";
    private const string PemEndMarker = "-----END PRIVATE KEY-----";

    /// <summary>
    /// Build an <see cref="RsaCrypto"/> from a base64-encoded PKCS#8 string
    /// (with or without PEM markers).
    /// </summary>
    public static RsaCrypto FromBase64Pkcs8(string base64)
    {
        var cleaned = base64
            .Replace(PemBeginMarker, string.Empty)
            .Replace(PemEndMarker, string.Empty)
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty)
            .Trim();
        return new RsaCrypto(Convert.FromBase64String(cleaned));
    }

    /// <summary>
    /// Build an <see cref="RsaCrypto"/> from a PKCS#8 file on disk. Both
    /// DER- and PEM-encoded files are accepted; PEM files are routed through
    /// <see cref="FromBase64Pkcs8"/> so the BEGIN/END markers are stripped
    /// before the body is base64-decoded. Reading a PEM file through the
    /// raw-bytes path would otherwise raise a <see cref="CryptographicException"/>
    /// from <see cref="RSA.ImportPkcs8PrivateKey"/>
    /// </summary>
    public static RsaCrypto FromPkcs8File(string path)
    {
        var contents = File.ReadAllText(path);

        if (contents.Contains(PemBeginMarker, StringComparison.Ordinal))
            return FromBase64Pkcs8(contents);

        return new RsaCrypto(File.ReadAllBytes(path));
    }

    /// <summary>
    /// Decrypts a base64-encoded password that the client encrypted with
    /// PKCS#1 v1.5 padding under the matching public key.
    /// </summary>
    public string DecryptPassword(string base64Cipher)
    {
        var cipher = Convert.FromBase64String(base64Cipher);
        var plain = _privateKey.Decrypt(cipher, RSAEncryptionPadding.Pkcs1);
        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>
    /// Tries to decrypt the supplied cipher. Returns <c>false</c> if the
    /// padding is invalid or the input is not valid base64.
    /// </summary>
    public bool TryDecryptPassword(string base64Cipher, out string plain)
    {
        try
        {
            plain = DecryptPassword(base64Cipher);
            return true;
        }
        catch
        {
            plain = string.Empty;
            return false;
        }
    }

    public void Dispose() => _privateKey.Dispose();
}
