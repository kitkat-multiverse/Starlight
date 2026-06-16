using Starlight.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Starlight.Database.DependencyInjection;
using Starlight.SDK.Database;
using Starlight.SDK.Database.Impl;

namespace Starlight.SDK;

public sealed class HttpServerService(StarlightConfig config) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var builder = WebApplication.CreateBuilder();

        builder.Services
            .AddSerilog()
            .AddSingleton(_ => config);

        // NOTE: If you wish to use SSL, do so behind a reverse proxy.
        builder.WebHost.UseUrls($"http://{config.Server.Http.BindAddress}:{config.Server.Http.BindPort}");

        var app = builder.Build();

        app.MapGet("/", () => Results.Ok("Starlight"));

        await app.RunAsync(stoppingToken);
    }
}

public static class ServiceExtensions
{
    public static IServiceCollection AddSdkServer(this IServiceCollection collection, StarlightConfig config)
    {
        var provider = DatabaseHelper.ParseProvider(config.Database.ConnectionString, out var connString);
        switch (provider)
        {
            case ProviderType.Sqlite: {
                collection.AddStarlightDatabase(connString, config.Database.Sqlite, typeof(HttpServerService).Assembly);
                collection.AddSingleton<IAccountRepository, SqliteAccountRepository>();
                break;
            }
            default:
                throw new NotSupportedException($"Unsupported or missing database provider '{provider?.ToString() ?? "<null>"}'.");
        }

        collection.AddHostedService<HttpServerService>();

        return collection;
    }
}
