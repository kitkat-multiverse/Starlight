using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Starlight.Protobuf.DependencyInjection;
using Starlight.Protobuf.Fixtures;
using Starlight.Protobuf.Fixtures.V99;
using Starlight.Protobuf.Registry;
using Xunit;
using IMessage = Starlight.Protobuf.Core.IMessage;

namespace Starlight.Protobuf.Tests;

/// <summary>
///     Version detection + DI initialization, driven entirely by the synthetic
///     fixtures (no live protocol). Covers assembly discovery, first-packet
///     resolution, version lookup, collision tie-break, and the
///     <c>AddStarlightProtocol</c> DI extension.
/// </summary>
public sealed class VersionDetectionTests
{
    // The first-packet cmd id is version-specific and lives on the registry, not
    // the canonical base POCO. PingReq is a first-packet message in the fixtures.
    private static readonly int PingReqCmdId;

    // The compiled fixture registry lives in the fixtures assembly. Ambient
    // discovery (AppDomain.GetAssemblies) only sees *loaded* assemblies, and an
    // assembly loads lazily on first type use. The explicit static ctor both
    // loads it and (by disabling beforefieldinit) runs before the first test, so
    // even version-string-only lookups find V99.
    static VersionDetectionTests()
    {
        var registry = new V99ProtocolRegistry();
        PingReqCmdId = registry.GetCmdId(new PingReq());
    }

    private static IProtocolRegistryProvider Provider() =>
        new ProtocolRegistryProvider(ProtocolHelper.DiscoverRegistries());

    [Fact]
    public void Discover_FindsCompiledV99Registry()
    {
        var registries = ProtocolHelper.DiscoverRegistries();
        Assert.Contains(registries, r => r is V99ProtocolRegistry);
    }

    [Fact]
    public void ResolveByFirstPacket_ReturnsV99_ForPingReq()
    {
        var registry = Provider().ResolveByFirstPacket(PingReqCmdId);

        Assert.NotNull(registry);
        Assert.Equal("V99", registry!.Version);
    }

    [Fact]
    public void ResolveByFirstPacket_ReturnsNull_ForUnknownCmdId()
    {
        Assert.Null(Provider().ResolveByFirstPacket(-1));
    }

    [Fact]
    public void GetByVersion_IsCaseInsensitive()
    {
        var provider = Provider();
        Assert.NotNull(provider.GetByVersion("V99"));
        Assert.NotNull(provider.GetByVersion("v99"));
        Assert.Null(provider.GetByVersion("V123"));
    }

    [Fact]
    public void FirstPacketCollision_PrefersNewestVersion()
    {
        const int sharedCmdId = 4242;

        var provider = new ProtocolRegistryProvider(
        [
            new FakeRegistry("V64", sharedCmdId),
            new FakeRegistry("V66", sharedCmdId),
            new FakeRegistry("V65", sharedCmdId)
        ]);

        Assert.Equal("V66", provider.ResolveByFirstPacket(sharedCmdId)!.Version);
    }

    [Fact]
    public void DuplicateVersion_Throws_WithBothTypesNamed()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ProtocolRegistryProvider(
            [
                new FakeRegistry("V66", 1),
                new FakeRegistry("V66", 2)
            ]));

        Assert.Contains("V66", ex.Message);
    }

    [Fact]
    public void AddStarlightProtocol_RegistersResolvableSingletonProvider()
    {
        var services = new ServiceCollection();
        services.AddStarlightProtocol();
        using var sp = services.BuildServiceProvider();

        var provider = sp.GetRequiredService<IProtocolRegistryProvider>();

        Assert.Same(provider, sp.GetRequiredService<IProtocolRegistryProvider>());
        Assert.Equal("V99", provider.ResolveByFirstPacket(PingReqCmdId)!.Version);
    }

    private sealed class FakeRegistry(string version, params int[] knownFirst) : ProtocolRegistry
    {
        public override string Version { get; } = version;
        public override IReadOnlySet<int> KnownFirst { get; } = new HashSet<int>(knownFirst);

        public override int GetCmdId(IMessage message) => throw new NotSupportedException();
        public override IMessage Create(int cmdId) => throw new NotSupportedException();
        public override int CalculateSize(IMessage message) => throw new NotSupportedException();

        public override void Serialize(IMessage message, CodedOutputStream output) =>
            throw new NotSupportedException();

        public override void Deserialize(IMessage message, CodedInputStream input) =>
            throw new NotSupportedException();
    }
}
