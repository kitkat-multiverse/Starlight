using System.Security.Cryptography;

namespace Starlight.Crypto;

/// <summary>
///     RSA helper for the dispatch region payload. Encrypts the region content with
///     the client's content public key (selected by the request's <c>key_id</c>) and
///     signs the plaintext with the dispatch signing private key. The content keys
///     and the signing key are different keypairs, mirroring the official
///     query_cur_region response.
/// </summary>
public sealed class DispatchRsaCrypto : IDisposable
{
    /// <summary>
    /// </summary>
    /// <param name="signingKey">Signing private key; pass <c>null</c> to disable signing.</param>
    /// <param name="encryptKeyPems">
    ///     Map of <c>key_id</c> to PEM-encoded content key. PKCS#1/PKCS#8 private keys
    ///     and SPKI public keys are all accepted; only the public component is used
    ///     for encryption.
    /// </param>
    public DispatchRsaCrypto(RSA? signingKey, IReadOnlyDictionary<int, string>? encryptKeyPems = null)
    {
        SigningKey = signingKey;

        if (encryptKeyPems is { Count: > 0 })
        {
            var map = new Dictionary<int, RSA>(encryptKeyPems.Count);

            try
            {
                foreach (var (id, pem) in encryptKeyPems)
                {
                    var rsa = RSA.Create();

                    try
                    {
                        rsa.ImportFromPem(pem);
                    }
                    catch
                    {
                        rsa.Dispose();
                        throw;
                    }
                    map[id] = rsa;
                }
            }
            catch
            {
                foreach (var key in map.Values)
                {
                    key.Dispose();
                }
                SigningKey?.Dispose();
                throw;
            }
            ContentKeys = map;
        } else
        {
            ContentKeys = new Dictionary<int, RSA>();
        }
    }

    /// <summary>Whether a signing key was loaded and <see cref="GenerateSignature" /> can be used.</summary>
    public bool CanSign => SigningKey is not null;

    /// <summary>The signing ('cur') private key, or <c>null</c> if none was loaded.</summary>
    public RSA? SigningKey { get; }

    /// <summary>The content encryption keys indexed by <c>key_id</c>.</summary>
    public IReadOnlyDictionary<int, RSA> ContentKeys { get; }

    public void Dispose()
    {
        SigningKey?.Dispose();

        foreach (var key in ContentKeys.Values)
        {
            key.Dispose();
        }
    }

    /// <summary>
    ///     Build a <see cref="DispatchRsaCrypto" /> from an optional signing key file
    ///     (PKCS#1/PKCS#8 PEM or PKCS#8 DER) and an optional map of content keys
    ///     indexed by <c>key_id</c>.
    /// </summary>
    public static DispatchRsaCrypto Create(string? signingKeyPath, IReadOnlyDictionary<int, string>? encryptKeyPems = null)
    {
        var signing = string.IsNullOrWhiteSpace(signingKeyPath) ? null : RsaKeyLoader.LoadPrivateKeyFile(signingKeyPath);
        return new DispatchRsaCrypto(signing, encryptKeyPems);
    }

    /// <summary>
    ///     Encrypts the region payload with the content key matching <paramref name="keyId" />.
    ///     Returns <c>false</c> if no key is registered for that id.
    /// </summary>
    public bool TryEncryptPayload(byte[] data, int keyId, out string payload)
    {
        if (!ContentKeys.TryGetValue(keyId, out var key))
        {
            payload = string.Empty;
            return false;
        }

        // RSA can only encrypt blocks up to (modulus - padding) bytes, so the
        // payload is split into chunks of keySize/8 - 11 (the PKCS#1 v1.5
        // overhead) and each encrypted block is concatenated, matching the
        // client's chunked decryption.
        var chunkSize = key.KeySize / 8 - 11;

        using var output = new MemoryStream();

        for (var offset = 0; offset < data.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, data.Length - offset);

            var encrypted = key.Encrypt(
                data.AsSpan(offset, length).ToArray(),
                RSAEncryptionPadding.Pkcs1);
            output.Write(encrypted, offset: 0, encrypted.Length);
        }

        payload = Convert.ToBase64String(output.ToArray());
        return true;
    }

    /// <summary>
    ///     Decrypts a single RSA block with the signing ('cur') private key
    ///     (PKCS#1 v1.5). Used to recover the client's random seed from
    ///     <c>client_rand_key</c>.
    /// </summary>
    public byte[] DecryptWithSigningKey(byte[] cipher)
    {
        if (SigningKey is null)
        {
            throw new InvalidOperationException("No signing key was loaded; DecryptWithSigningKey is unavailable.");
        }

        return SigningKey.Decrypt(cipher, RSAEncryptionPadding.Pkcs1);
    }

    /// <summary>
    ///     Tries to decrypt a single RSA block with the signing ('cur') private key.
    ///     Returns <c>false</c> if no signing key is loaded or the padding/input is
    ///     invalid.
    /// </summary>
    public bool TryDecryptWithSigningKey(byte[] cipher, out byte[] plain)
    {
        if (SigningKey is not null)
        {
            try
            {
                plain = SigningKey.Decrypt(cipher, RSAEncryptionPadding.Pkcs1);
                return true;
            }
            catch (CryptographicException)
            {}
        }

        plain = [];
        return false;
    }

    /// <summary>
    ///     Tries to decrypt a single RSA block with the content key matching
    ///     <paramref name="keyId" /> (PKCS#1 v1.5). Returns <c>false</c> if no key is
    ///     registered for that id or the padding/input is invalid.
    /// </summary>
    public bool TryDecryptContent(int keyId, byte[] cipher, out byte[] plain)
    {
        if (ContentKeys.TryGetValue(keyId, out var key))
        {
            try
            {
                plain = key.Decrypt(cipher, RSAEncryptionPadding.Pkcs1);
                return true;
            }
            catch (CryptographicException)
            {}
        }

        plain = [];
        return false;
    }

    /// <summary>
    ///     Signs the given data with the signing private key (SHA-256 / PKCS#1 v1.5).
    /// </summary>
    /// <returns>A base64-encoded signature for the given data.</returns>
    public string GenerateSignature(byte[] data)
    {
        if (SigningKey is null)
        {
            throw new InvalidOperationException("No signing key was loaded; GenerateSignature is unavailable.");
        }

        var signature = SigningKey.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(signature);
    }
}
