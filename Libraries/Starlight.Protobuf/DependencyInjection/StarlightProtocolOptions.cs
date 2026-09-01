using Starlight.Protobuf.Registry;
using System.Reflection;

namespace Starlight.Protobuf.DependencyInjection;

/// <summary>Configures <c>AddStarlightProtocol</c> registry discovery.</summary>
public sealed class StarlightProtocolOptions
{
    /// <summary>
    /// Assemblies to scan for compiled <see cref="ProtocolRegistry"/> subclasses.
    /// Defaults to every assembly loaded in the current <see cref="AppDomain"/>.
    /// </summary>
    public IEnumerable<Assembly>? Assemblies { get; set; }

    /// <summary>
    /// Optional directory of plugin <c>.dll</c>s to load and scan for additional
    /// registries (older / externally shipped protocol versions). <c>null</c> to
    /// skip plugin loading.
    /// </summary>
    public string? PluginDirectory { get; set; }

    /// <summary>
    /// When <c>true</c>, the discovered provider also becomes
    /// <see cref="ProtocolHelper.Default"/> so the static API and DI share one
    /// index. Defaults to <c>true</c>.
    /// </summary>
    public bool SetAsDefault { get; set; } = true;
}
