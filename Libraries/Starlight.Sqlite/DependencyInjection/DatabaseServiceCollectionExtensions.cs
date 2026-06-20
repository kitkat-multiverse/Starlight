using System.Reflection;
using Starlight.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Starlight.Database.DependencyInjection;

public static class DatabaseServiceCollectionExtensions
{
    public static IServiceCollection AddStarlightDatabase(this IServiceCollection services, StarlightDatabaseOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<StarlightDatabase>();

        services.AddSingleton<IStarlightDatabase>(sp => {
            var database = sp.GetRequiredService<StarlightDatabase>();
            Database.Instance = database;
            return database;
        });
        services.AddHostedService<StarlightDatabaseHostedService>();

        return services;
    }

    public static IServiceCollection AddStarlightDatabase(
        this IServiceCollection services,
        StarlightDatabaseOptions config,
        params Assembly[] modelAssemblies
    )
    {
        foreach (var assembly in modelAssemblies.Distinct())
        {
            config.ModelAssemblies.Add(assembly);
        }

        return services.AddStarlightDatabase(config);
    }
}

internal sealed class StarlightDatabaseHostedService(IStarlightDatabase database) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => database.InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (database is IAsyncDisposable asyncDisposable)
            return asyncDisposable.DisposeAsync().AsTask();

        database.Dispose();
        return Task.CompletedTask;
    }
}
