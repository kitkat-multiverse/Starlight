using System.Reflection;
using Starlight.Common;
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

    public static IServiceCollection AddStarlightDatabase(this IServiceCollection services, string path, SqliteConfig config, params Assembly[] modelAssemblies)
    {
        return services.AddStarlightDatabase(options => {
            options.Path = path;
            options.CreateIfMissing = config.CreateIfMissing;
            options.UseWal = config.UseWal;
            options.Synchronous = config.Synchronous;
            options.BusyTimeoutMilliseconds = config.BusyTimeoutMilliseconds;
            options.AllowClientEvaluation = config.AllowClientEvaluation;

            foreach (var assembly in modelAssemblies.Distinct())
            {
                options.ModelAssemblies.Add(assembly);
            }
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
