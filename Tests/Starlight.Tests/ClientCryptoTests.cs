using Starlight.Crypto.Client;
using Xunit;

namespace Starlight.Tests;

public sealed class ClientCryptoTests
{
    [Fact]
    public void EmbeddedContentKeysAreDiscoveredById()
    {
        using var crypto = ClientCrypto.Create(generateRsaKeys: false);

        Assert.Equal([2, 3, 4, 5], crypto.ContentKeys.Keys.Order());
    }

    [Fact]
    public void MissingConfiguredSigningKeyFailsWhenGenerationIsDisabled()
    {
        var path = Path.Combine(Path.GetTempPath(), $"starlight-missing-{Guid.NewGuid():N}.pem");
        var options = new ClientCryptoOptions { SigningKeyPath = path };

        var error = Assert.Throws<FileNotFoundException>(() =>
            ClientCrypto.Create(generateRsaKeys: false, options));

        Assert.Equal(path, error.FileName);
    }

    [Fact]
    public void MissingConfiguredSdkKeyFailsWhenGenerationIsDisabled()
    {
        var path = Path.Combine(Path.GetTempPath(), $"starlight-missing-{Guid.NewGuid():N}.pem");
        var options = new ClientCryptoOptions { SdkKeyPath = path };

        var error = Assert.Throws<FileNotFoundException>(() =>
            ClientCrypto.Create(generateRsaKeys: false, options));

        Assert.Equal(path, error.FileName);
    }
}
