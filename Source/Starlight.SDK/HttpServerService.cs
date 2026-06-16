using Starlight.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

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
