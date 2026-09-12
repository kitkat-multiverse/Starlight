using System.Reflection;

namespace Starlight.Protobuf.Registry;

/// <summary>
///     Static convenience over protocol-registry discovery and resolution. Mirrors
///     <see cref="IProtocolRegistryProvider" /> for callers that aren't using DI. The
///     default provider lazily scans the currently loaded assemblies on first use.
/// </summary>
public static class ProtocolHelper
{
    private static Lazy<IProtocolRegistryProvider> _default = new(() => new ProtocolRegistryProvider(DiscoverRegistries()));

    /// <summary>
    ///     The process-wide provider. Reads lazily by scanning loaded assemblies;
    ///     can be replaced (e.g. by the DI extension) so the static and DI paths
    ///     share one index.
    /// </summary>
    public static IProtocolRegistryProvider Default
    {
        get => _default.Value;
        set => _default = new Lazy<IProtocolRegistryProvider>(value);
    }

    /// <inheritdoc cref="IProtocolRegistryProvider.ResolveByFirstPacket" />
    public static ProtocolRegistry? ResolveByFirstPacket(int cmdId) => Default.ResolveByFirstPacket(cmdId);

    /// <inheritdoc cref="IProtocolRegistryProvider.GetByVersion" />
    public static ProtocolRegistry? GetByVersion(string version) => Default.GetByVersion(version);

    /// <summary>
    ///     Instantiates every concrete <see cref="ProtocolRegistry" /> subclass found
    ///     in <paramref name="assemblies" /> (default: all loaded assemblies). Types
    ///     that fail to load or construct are skipped rather than aborting the scan.
    /// </summary>
    public static IReadOnlyList<ProtocolRegistry> DiscoverRegistries(IEnumerable<Assembly>? assemblies = null)
    {
        assemblies ??= AppDomain.CurrentDomain.GetAssemblies();

        var found = new List<ProtocolRegistry>();

        foreach (var assembly in assemblies)
        {
            CollectFrom(assembly, found);
        }

        return found;
    }

    /// <summary>
    ///     Loads every <c>.dll</c> in <paramref name="directory" /> and returns the
    ///     <see cref="ProtocolRegistry" /> instances they contribute. A missing
    ///     directory yields an empty result.
    /// </summary>
    public static IReadOnlyList<ProtocolRegistry> LoadFromDirectory(string directory)
    {
        var found = new List<ProtocolRegistry>();
        if (!Directory.Exists(directory)) return found;

        foreach (var path in Directory.EnumerateFiles(directory, "*.dll"))
        {
            Assembly assembly;

            try
            {
                assembly = Assembly.LoadFrom(path);
            }
            catch
            {
                continue; // not a managed assembly / load failure -> skip
            }

            CollectFrom(assembly, found);
        }

        return found;
    }

    private static void CollectFrom(Assembly assembly, List<ProtocolRegistry> sink)
    {
        Type[] types;

        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).ToArray()!;
        }
        catch
        {
            return;
        }

        foreach (var type in types)
        {
            if (type is null || type.IsAbstract || !typeof(ProtocolRegistry).IsAssignableFrom(type))
                continue;

            if (type.GetConstructor(Type.EmptyTypes) is null)
                continue;

            try
            {
                if (Activator.CreateInstance(type) is ProtocolRegistry registry)
                    sink.Add(registry);
            }
            catch
            {
                // ignore registries that fail to construct
            }
        }
    }
}
