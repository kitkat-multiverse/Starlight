using Microsoft.Extensions.DependencyInjection;
using Starlight.Common;
using Starlight.Database.DependencyInjection;
using Starlight.SDK.Database;
using Starlight.SDK.Database.Impl;

namespace Starlight.SDK;

public static class ServiceExtensions
{
    public static IServiceCollection AddSdkServer(this IServiceCollection services)
    {
        var config = Config.Instance;

        switch (DatabaseHelper.ParseProvider(config.Database.ConnectionString, out var connString))
        {
            case ProviderType.Sqlite: {
                services.AddStarlightDatabase(connString, config.Database.Sqlite, typeof(HttpServerService).Assembly);
                services.AddSingleton<IAccountRepository, SqliteAccountRepository>();
                break;
            }
        }
        
        services.AddHostedService<HttpServerService>();
        
        return services;
    }
}
