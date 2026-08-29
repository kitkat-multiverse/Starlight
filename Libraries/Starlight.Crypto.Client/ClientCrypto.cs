using System.Reflection;
using System.Security.Cryptography;

namespace Starlight.Crypto.Client;

/// <summary>
/// Central holder for the client-facing RSA keys used by the SDK and dispatch
/// flow: the per-<c>key_id</c> content keys, the dispatch signing ('cur') key,
/// and the SDK password key. Keys are loaded from embedded resources by
/// default; a configured filesystem path overrides the embedded key when set.
/// Inject this to access any of the keys or to run the encrypt/sign/decrypt
/// operations that depend on them.
/// </summary>
public sealed class ClientCrypto : IDisposable
{
    private const string ResourcePrefix = "Starlight.Crypto.Client.Resources";
    private const string ContentKeyPrefix = ResourcePrefix + ".Keys";
    private const string PemSuffix = "pem";

    private readonly DispatchRsaCrypto _dispatch;
    private readonly RsaCrypto _sdk;

    private ClientCrypto(DispatchRsaCrypto dispatch, RsaCrypto sdk)
    {
        _dispatch = dispatch;
        _sdk = sdk;
    }

    /// <summary>Content encryption keys indexed by <c>key_id</c>.</summary>
    public IReadOnlyDictionary<int, RSA> ContentKeys => _dispatch.ContentKeys;

    /// <summary>The dispatch signing ('cur') key, or <c>null</c> if unavailable.</summary>
    public RSA? SigningKey => _dispatch.SigningKey;

    /// <summary>The SDK password-decryption key.</summary>
    public RSA SdkKey => _sdk.PrivateKey;

    /// <summary>Whether a signing key is available and <see cref="GenerateSignature"/> can be used.</summary>
    public bool CanSign => _dispatch.CanSign;

    /// <summary>
    /// Builds a <see cref="ClientCrypto"/> from the embedded keys, honoring any
    /// filesystem-path overrides supplied in <paramref name="options"/>.
    /// </summary>
    public static ClientCrypto Create(bool generateRsaKeys, ClientCryptoOptions? options = null)
    {
        options ??= new ClientCryptoOptions();
        var assembly = typeof(ClientCrypto).Assembly;
        var basePath = options.BasePath ?? Directory.GetCurrentDirectory();

        var signingKeyPath = ResolvePath(options.SigningKeyPath, basePath);
        var sdkKeyPath = ResolvePath(options.SdkKeyPath, basePath);

        var contentKeys = LoadContentKeys(assembly);
        var signingKey = LoadOrCreateSigningKey(assembly, generateRsaKeys, signingKeyPath, basePath);

        RsaCrypto sdk;

        try
        {
            sdk = LoadOrCreateSdkKey(assembly, generateRsaKeys, sdkKeyPath, basePath);
        }
        catch
        {
            signingKey?.Dispose();
            throw;
        }

        DispatchRsaCrypto dispatch;

        try
        {
            dispatch = new DispatchRsaCrypto(signingKey, contentKeys);
        }
        catch
        {
            // DispatchRsaCrypto disposes signingKey on failure; only the sdk key
            // is still owned here.
            sdk.Dispose();
            throw;
        }

        return new ClientCrypto(dispatch, sdk);
    }

    /// <summary>
    /// Encrypts the payload with the content key matching <paramref name="keyId"/>.
    /// Returns <c>false</c> if no key is registered for that id.
    /// </summary>
    public bool TryEncryptPayload(byte[] data, int keyId, out string payload)
        => _dispatch.TryEncryptPayload(data, keyId, out payload);

    /// <summary>
    /// Decrypts a single RSA block with the signing ('cur') key (PKCS#1 v1.5).
    /// Used to recover the client's random seed from <c>client_rand_key</c>.
    /// </summary>
    public byte[] DecryptWithSigningKey(byte[] cipher) => _dispatch.DecryptWithSigningKey(cipher);

    /// <summary>
    /// Tries to decrypt a single RSA block with the signing ('cur') key. Returns
    /// <c>false</c> if no signing key is available or the padding/input is invalid.
    /// </summary>
    public bool TryDecryptWithSigningKey(byte[] cipher, out byte[] plain)
        => _dispatch.TryDecryptWithSigningKey(cipher, out plain);

    /// <summary>
    /// Tries to decrypt a single RSA block with the content key matching
    /// <paramref name="keyId"/>. Returns <c>false</c> if no key is registered for
    /// that id or the padding/input is invalid.
    /// </summary>
    public bool TryDecryptContent(int keyId, byte[] cipher, out byte[] plain)
        => _dispatch.TryDecryptContent(keyId, cipher, out plain);

    /// <summary>Signs the data with the signing key (SHA-256 / PKCS#1 v1.5).</summary>
    public string GenerateSignature(byte[] data) => _dispatch.GenerateSignature(data);

    /// <summary>Decrypts a base64-encoded password using the SDK key.</summary>
    public string DecryptPassword(string base64Cipher) => _sdk.Decrypt(base64Cipher);

    /// <summary>
    /// Tries to decrypt the supplied cipher with the SDK key. Returns <c>false</c>
    /// if the padding is invalid or the input is not valid base64.
    /// </summary>
    public bool TryDecryptPassword(string base64Cipher, out string plain)
        => _sdk.TryDecrypt(base64Cipher, out plain);

    public void Dispose()
    {
        _dispatch.Dispose();
        _sdk.Dispose();
    }

    private static Dictionary<int, string> LoadContentKeys(Assembly assembly)
    {
        var keys = new Dictionary<int, string>();

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ContentKeyPrefix, StringComparison.Ordinal)
                || !name.EndsWith(PemSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            var idText = name[ContentKeyPrefix.Length..^PemSuffix.Length].Trim('.');

            if (int.TryParse(idText, out var keyId))
            {
                keys[keyId] = ReadResource(assembly, name);
            }
        }

        return keys;
    }

    private static RSA LoadOrCreateSigningKey(Assembly assembly, bool generateRsaKeys, string? path, string basePath)
    {
        if (path is not null)
        {
            if (File.Exists(path))
            {
                return RsaKeyLoader.LoadPrivateKeyFile(path);
            }

            if (!generateRsaKeys)
            {
                throw new FileNotFoundException("The configured signing key does not exist.", path);
            }

            EnsureRsaKeyExists(path, keySize: 2048, RsaKeyExportFormat.Pkcs8Pem);
            return RsaKeyLoader.LoadPrivateKeyFile(path);
        }

        if (!generateRsaKeys)
        {
            var pem = ReadResource(assembly, $"{ResourcePrefix}.signing.pem");
            var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            return rsa;
        }

        path = Path.Combine(basePath, "keys", "signing.pem");

        EnsureRsaKeyExists(path, keySize: 2048, RsaKeyExportFormat.Pkcs8Pem);

        return RsaKeyLoader.LoadPrivateKeyFile(path);
    }

    private static RsaCrypto LoadOrCreateSdkKey(Assembly assembly, bool generateRsaKeys, string? path, string basePath)
    {
        if (path is not null)
        {
            if (File.Exists(path))
            {
                return RsaCrypto.FromBase64Pkcs8(File.ReadAllText(path));
            }

            if (!generateRsaKeys)
            {
                throw new FileNotFoundException("The configured SDK password key does not exist.", path);
            }

            EnsureRsaKeyExists(path, keySize: 2048, RsaKeyExportFormat.Pkcs8Base64);
            return RsaCrypto.FromBase64Pkcs8(File.ReadAllText(path));
        }

        if (!generateRsaKeys)
        {
            var key = ReadResource(assembly, $"{ResourcePrefix}.sdk.pem");
            return RsaCrypto.FromBase64Pkcs8(key);
        }

        path = Path.Combine(basePath, "keys", "sdk.pem");

        EnsureRsaKeyExists(path, keySize: 2048, RsaKeyExportFormat.Pkcs8Base64);

        return RsaCrypto.FromBase64Pkcs8(File.ReadAllText(path));
    }

    private static string? ResolvePath(string? path, string basePath)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Path.GetFullPath(path, basePath);
    }

    private static void EnsureRsaKeyExists(
        string path,
        int keySize,
        RsaKeyExportFormat exportFormat
    )
    {
        if (File.Exists(path))
        {
            return;
        }

        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var rsa = RSA.Create(keySize);

        var key = exportFormat switch {
            RsaKeyExportFormat.Pkcs8Pem =>
                rsa.ExportPkcs8PrivateKeyPem(),

            RsaKeyExportFormat.Pkcs8Base64 =>
                Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()),

            _ => throw new ArgumentOutOfRangeException(nameof(exportFormat))
        };

        File.WriteAllText(path, key);
    }

    private static string ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"Embedded crypto resource '{name}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private enum RsaKeyExportFormat
    {
        Pkcs8Pem,
        Pkcs8Base64
    }
}
