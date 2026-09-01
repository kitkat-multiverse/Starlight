using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Starlight.Game.Ability.HpDebts;
using System.Reflection;

namespace Starlight.Game.Ability.Handlers;

public static class AbilityServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ability invoke dispatcher and finds all <see cref="IAbilityInvokeHandler"/> implementations in the given assemblies.
    /// If none are given, the current ability assembly is used.
    /// </summary>
    public static IServiceCollection AddAbilityInvokeHandlers(
        this IServiceCollection services,
        params Assembly[] assemblies
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        if (assemblies.Length == 0)
            assemblies = [typeof(IAbilityInvokeHandler).Assembly];

        var contract = typeof(IAbilityInvokeHandler);

        foreach (var implementation in assemblies
                     .Distinct()
                     .SelectMany(static assembly => assembly.GetTypes())
                     .Where(type =>
                         type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false } &&
                         contract.IsAssignableFrom(type)))
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton(contract, implementation));
        }

        services.TryAddSingleton<HpDebtService>();
        services.TryAddSingleton<SwitchHealToHpDebtsProcessor>();
        services.TryAddSingleton<AbilityInvokeHandlerRegistry>();
        return services;
    }
}
