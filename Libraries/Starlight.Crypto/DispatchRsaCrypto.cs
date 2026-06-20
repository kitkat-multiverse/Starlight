using System.Security.Cryptography;

namespace Starlight.Crypto;

/// <summary>
/// RSA helper for the dispatch region payload. Encrypts the region content with
/// the client's content public key (selected by the request's <c>key_id</c>) and
/// signs the plaintext with the dispatch signing private key. The content keys
/// and the signing key are different keypairs, mirroring the official
/// query_cur_region response.
/// </summary>
public sealed class DispatchRsaCrypto : IDisposable
{
    private readonly RSA? _signingKey;
    private readonly IReadOnlyDictionary<int, RSA> _encryptKeys;

    /// <summary>Whether a signing key was loaded and <see cref="GenerateSignature"/> can be used.</summary>
    public bool CanSign => _signingKey is not null;

    /// <summary>
    /// </summary>
    /// <param name="signingKey">Signing private key; pass <c>null</c> to disable signing.</param>
    /// <param name="encryptKeyPems">
    /// Map of <c>key_id</c> to PEM-encoded content key. PKCS#1/PKCS#8 private keys
    /// and SPKI public keys are all accepted; only the public component is used
    /// for encryption.
    /// </param>
    public DispatchRsaCrypto(RSA? signingKey, IReadOnlyDictionary<int, string>? encryptKeyPems = null)
    {
        _signingKey = signingKey;

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
                _signingKey?.Dispose();
                throw;
            }
            _encryptKeys = map;
        } else
        {
            _encryptKeys = new Dictionary<int, RSA>();
        }
    }

    /// <summary>
    /// Build a <see cref="DispatchRsaCrypto"/> from an optional signing key file
    /// (PKCS#1/PKCS#8 PEM or PKCS#8 DER) and an optional map of content keys
    /// indexed by <c>key_id</c>.
    /// </summary>
    public static DispatchRsaCrypto Create(string? signingKeyPath, IReadOnlyDictionary<int, string>? encryptKeyPems = null)
    {
        var signing = string.IsNullOrWhiteSpace(signingKeyPath) ? null : RsaKeyLoader.LoadPrivateKeyFile(signingKeyPath);
        return new DispatchRsaCrypto(signing, encryptKeyPems);
    }

    /// <summary>
    /// Encrypts the region payload with the content key matching <paramref name="keyId"/>.
    /// Returns <c>false</c> if no key is registered for that id.
    /// </summary>
    public bool TryEncryptPayload(byte[] data, int keyId, out string payload)
    {
        if (!_encryptKeys.TryGetValue(keyId, out var key))
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
    /// Signs the given data with the signing private key (SHA-256 / PKCS#1 v1.5).
    /// </summary>
    /// <returns>A base64-encoded signature for the given data.</returns>
    public string GenerateSignature(byte[] data)
    {
        if (_signingKey is null)
        {
            throw new InvalidOperationException("No signing key was loaded; GenerateSignature is unavailable.");
        }

        var signature = _signingKey.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(signature);
    }

    public void Dispose()
    {
        _signingKey?.Dispose();

        foreach (var key in _encryptKeys.Values)
        {
            key.Dispose();
        }
    }
}
