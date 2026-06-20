using System.Security.Cryptography;
using System.Text;

namespace Starlight.Crypto;

/// <summary>
/// RSA helper for decrypting account/password fields that a client encrypts
/// with PKCS#1 v1.5 padding under the matching public key.
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

    /// <summary>
    /// Build an <see cref="RsaCrypto"/> from a base64-encoded PKCS#8 string
    /// (with or without PEM markers).
    /// </summary>
    public static RsaCrypto FromBase64Pkcs8(string base64)
        => new(RsaKeyLoader.DecodePkcs8(base64));

    /// <summary>
    /// Build an <see cref="RsaCrypto"/> from a PKCS#8 file on disk. Both DER-
    /// and PEM-encoded files are accepted.
    /// </summary>
    public static RsaCrypto FromPkcs8File(string path)
        => new(RsaKeyLoader.ReadPkcs8File(path));

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
