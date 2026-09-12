namespace Starlight.Protobuf.Registry;

/// <summary>
///     Default <see cref="IProtocolRegistryProvider" />: indexes a fixed set of
///     registries by version string and by their first-packet CmdIds. Immutable and
///     thread-safe after construction.
/// </summary>
public sealed class ProtocolRegistryProvider : IProtocolRegistryProvider
{
    private readonly Dictionary<int, ProtocolRegistry> _byFirstPacket;
    private readonly Dictionary<string, ProtocolRegistry> _byVersion;

    public ProtocolRegistryProvider(IEnumerable<ProtocolRegistry> registries)
    {
        _byVersion = new Dictionary<string, ProtocolRegistry>(StringComparer.OrdinalIgnoreCase);
        _byFirstPacket = new Dictionary<int, ProtocolRegistry>();

        foreach (var registry in registries)
        {
            // A duplicate version is a build/deploy misconfiguration. Discovery order
            // (AppDomain.GetAssemblies / GetTypes) is non-deterministic, so silently
            // keeping one would pick a different winner per run — fail loud instead.
            if (_byVersion.TryGetValue(registry.Version, out var duplicate))
                throw new InvalidOperationException(
                    $"Duplicate protocol registry version '{registry.Version}': " +
                    $"'{duplicate.GetType().FullName}' and '{registry.GetType().FullName}'.");

            _byVersion[registry.Version] = registry;

            foreach (var cmdId in registry.KnownFirst)
            {
                // Collision across versions: prefer the newest (highest version number).
                if (_byFirstPacket.TryGetValue(cmdId, out var existing) &&
                    VersionRank(existing) >= VersionRank(registry))
                    continue;

                _byFirstPacket[cmdId] = registry;
            }
        }

        Registries = _byVersion.Values.ToArray();
    }

    public IReadOnlyCollection<ProtocolRegistry> Registries { get; }

    public ProtocolRegistry? ResolveByFirstPacket(int cmdId) =>
        _byFirstPacket.GetValueOrDefault(cmdId);

    public ProtocolRegistry? GetByVersion(string version) =>
        _byVersion.GetValueOrDefault(version);

    /// <summary>Numeric rank of a version (the trailing digits of e.g. <c>"V66"</c>); higher = newer.</summary>
    private static int VersionRank(ProtocolRegistry registry)
    {
        var v = registry.Version;
        var i = v.Length;
        while (i > 0 && char.IsDigit(v[i - 1])) i--;
        return i < v.Length && int.TryParse(v.AsSpan(i), out var n) ? n : 0;
    }
}
