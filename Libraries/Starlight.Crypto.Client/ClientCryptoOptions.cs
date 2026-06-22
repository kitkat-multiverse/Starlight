namespace Starlight.Crypto.Client;

/// <summary>
/// Optional overrides for <see cref="ClientCrypto"/>. When a path is set and
/// the file exists, it replaces the corresponding embedded key.
/// </summary>
public sealed class ClientCryptoOptions
{
    /// <summary>Filesystem path overriding the embedded signing ('cur') key.</summary>
    public string? SigningKeyPath { get; set; }

    /// <summary>Filesystem path overriding the embedded SDK password key.</summary>
    public string? SdkKeyPath { get; set; }
}
