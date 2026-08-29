namespace Starlight.Crypto.Client;

/// <summary>
/// Optional filesystem settings for <see cref="ClientCrypto"/>. Configured paths
/// must exist when generation is disabled; when enabled, missing keys are generated.
/// </summary>
public sealed class ClientCryptoOptions
{
    /// <summary>
    /// Base directory used to resolve relative key paths and the generated
    /// <c>keys/signing.pem</c> and <c>keys/sdk.pem</c> defaults.
    /// </summary>
    public string? BasePath { get; set; }

    /// <summary>Filesystem path overriding the embedded signing ('cur') key.</summary>
    public string? SigningKeyPath { get; set; }

    /// <summary>Filesystem path overriding the embedded SDK password key.</summary>
    public string? SdkKeyPath { get; set; }
}
