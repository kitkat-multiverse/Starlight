using System.Reflection;
using Starlight.Common.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Starlight.Database.DependencyInjection;

public static class DatabaseServiceCollectionExtensions
{
    public static IServiceCollection AddStarlightDatabase(this IServiceCollection services, Action<StarlightDatabaseOptions>? configure = null)
    {
        var options = new StarlightDatabaseOptions();
        configure?.Invoke(options);

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

    public static IServiceCollection AddStarlightDatabase(this IServiceCollection services, StarlightConfig config, params Assembly[] modelAssemblies)
    {
        return services.AddStarlightDatabase(options => {
            options.Path = config.Database.Path;
            options.CreateIfMissing = config.Database.CreateIfMissing;
            options.UseWal = config.Database.UseWal;
            options.Synchronous = config.Database.Synchronous;
            options.BusyTimeoutMilliseconds = config.Database.BusyTimeoutMilliseconds;
            options.AllowClientEvaluation = config.Database.AllowClientEvaluation;

            foreach (var assembly in modelAssemblies.Distinct())
                options.ModelAssemblies.Add(assembly);
        });
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
