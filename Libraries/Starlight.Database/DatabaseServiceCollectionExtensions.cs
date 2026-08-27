using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Starlight.Database;

public static class DatabaseServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TContext"/> against the configured provider and makes
    /// sure its schema exists before the services that depend on it start.
    /// </summary>
    public static IServiceCollection AddStarlightDbContext<TContext>(
        this IServiceCollection services,
        DatabaseConfig config
    ) where TContext : DbContext
    {
        // Checked here rather than inside the options lambda; that doesn't run until something
        // first resolves the context, by which point startup has already reported success.
        if (config.Provider is not ProviderType.Sqlite)
            throw new NotSupportedException($"Unsupported database provider '{config.Provider}'.");

        services.AddDbContext<TContext>(opts => opts.UseSqlite(config.ConnectionString));

        return services.AddHostedService<DatabaseSchemaService<TContext>>();
    }
}
