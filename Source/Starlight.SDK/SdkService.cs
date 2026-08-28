using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using Starlight.Database.DependencyInjection;
using Starlight.Crypto.Client;
using Starlight.SDK.Database;
using Starlight.SDK.Database.Impl;
using Starlight.SDK.Http.Endpoints;
using Starlight.SDK.Services;

namespace Starlight.SDK;

public static partial class ServiceExtensions
{
    public static WebApplicationBuilder AddSdkServer(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration.GetSection("Sdk").Get<SdkConfig>() ?? new SdkConfig();

        builder.TrySetSdkKeyPath("SDK server", config.PasswordRsaKeyPath);

        switch (config.Database.Provider)
        {
            case ProviderType.Sqlite:
                {
                    builder.Services
                        .AddStarlightDatabase(config.Database.Sqlite, typeof(ServiceExtensions).Assembly)
                        .AddSingleton<IAccountRepository, SqliteAccountRepository>();
                    break;
                }
            default:
                throw new NotSupportedException($"Unsupported or missing database provider '{config.Database.Provider.ToString()}'.");
        }

        if (!config.SkipSignatureCheck && string.IsNullOrEmpty(config.HmacKey))
        {
            Log.Warning("SDK HMAC key is not configured but SkipSignatureCheck=false; combo-granter logins will fail with SystemError");
        }

        if (config.IpApi.Enabled)
        {
            builder.Services.AddHttpClient<IGeoIpLookup, IpApiGeoIpLookup>((sp, client) => {
                var cfg = sp.GetRequiredService<SdkConfig>();
                client.BaseAddress = new Uri(cfg.IpApi.Endpoint, UriKind.Absolute);
                client.Timeout = TimeSpan.FromMilliseconds(cfg.IpApi.TimeoutMilliseconds);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Starlight-SDK/1.0");
            });
        } else
        {
            builder.Services.TryAddSingleton<IGeoIpLookup, DefaultGeoIpLookup>();
        }

        builder.Services
            .AddSingleton(config)
            .AddSingleton<IAuthService, AuthService>();

        builder.WebHost.UseUrls($"http://{config.BindAddress}:{config.BindPort}");

        return builder;
    }

    public static IEndpointRouteBuilder MapSdkServer(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/", () => Results.Ok("Starlight"));
        builder.MapShieldEndpoints();
        builder.MapComboGranterEndpoints();
        builder.MapWebstaticEndpoints();
        builder.MapDeviceFingerprintEndpoints();
        builder.MapComboBoxEndpoints();
        builder.MapAbTestEndpoints();
        builder.MapPassportEndpoints();
        builder.MapLogEndpoints();
        builder.MapStarlightPatchEndpoints();
        return builder;
    }
}
