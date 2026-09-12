using Microsoft.Extensions.DependencyInjection;
using Starlight.Protobuf.Registry;

namespace Starlight.Protobuf.DependencyInjection;

/// <summary>DI wiring for the Starlight protocol layer.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Discovers every compiled <see cref="ProtocolRegistry" /> (and optional
    ///     plugin DLLs) and registers a singleton <see cref="IProtocolRegistryProvider" />.
    ///     Discovery runs once, eagerly, while building the provider singleton so the
    ///     server has its version index ready before it accepts connections.
    /// </summary>
    public static IServiceCollection AddStarlightProtocol(
        this IServiceCollection services,
        Action<StarlightProtocolOptions>? configure = null
    )
    {
        var options = new StarlightProtocolOptions();
        configure?.Invoke(options);

        services.AddSingleton<IProtocolRegistryProvider>(_ => {
            var registries = new List<ProtocolRegistry>(ProtocolHelper.DiscoverRegistries(options.Assemblies));

            if (!string.IsNullOrEmpty(options.PluginDirectory))
                registries.AddRange(ProtocolHelper.LoadFromDirectory(options.PluginDirectory));

            var provider = new ProtocolRegistryProvider(registries);

            if (options.SetAsDefault)
                ProtocolHelper.Default = provider;

            return provider;
        });

        return services;
    }
}
