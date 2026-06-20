using System.Diagnostics;
using Starlight.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using Starlight.Database.DependencyInjection;
using Starlight.Crypto;
using Starlight.SDK.Database;
using Starlight.SDK.Database.Impl;
using Starlight.SDK.Http.Endpoints;
using Starlight.SDK.Services;

namespace Starlight.SDK;

public static class ServiceExtensions
{
    public static WebApplicationBuilder AddSdkServer(this WebApplicationBuilder builder)
    {
        // TODO: Isolate database configuration to be per-service.
        var dbConfig = builder.Configuration.GetSection("Database").Get<DatabaseConfig>() ?? new DatabaseConfig();
        var config = builder.Configuration.GetSection("Sdk").Get<SdkConfig>() ?? new SdkConfig();

        var provider = DatabaseHelper.ParseProvider(dbConfig.ConnectionString, out var connString);

        switch (provider)
        {
            case ProviderType.Sqlite: {
                builder.Services
                    .AddStarlightDatabase(connString, dbConfig.Sqlite, typeof(ServiceExtensions).Assembly)
                    .AddSingleton<IAccountRepository, SqliteAccountRepository>();
                break;
            }
            default:
                throw new NotSupportedException($"Unsupported or missing database provider '{provider?.ToString() ?? "<null>"}'.");
        }

        // Load the password decryption key lazily, it's only needed when a
        // client sends is_crypto=true. Absence/invalidity is reported as
        // an explicit null so AuthService can distinguish "no key configured"
        // from "key loaded" and reject is_crypto=true requests up-front
        // rather than attempting decryption with a no-op instance.
        builder.Services.AddSingleton<RsaCrypto>(_ => {
            if (string.IsNullOrWhiteSpace(config.PasswordRsaKeyPath))
            {
                Log.Warning("SDK password RSA key path is not configured; is_crypto=true logins will be rejected");
                return null!;
            }

            if (!File.Exists(config.PasswordRsaKeyPath))
            {
                Log.Warning("Configured SDK password RSA key not found at {Path}", config.PasswordRsaKeyPath);
                return null!;
            }

            try
            {
                return RsaCrypto.FromPkcs8File(config.PasswordRsaKeyPath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load SDK password RSA key");
                return null!;
            }
        });

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

    public static IEndpointRouteBuilder MapSdkServer(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", () => Results.Ok("Starlight"));
        app.MapShieldEndpoints();
        app.MapComboGranterEndpoints();
        app.MapWebstaticEndpoints();
        app.MapDeviceFingerprintEndpoints();
        app.MapComboBoxEndpoints();
        app.MapAbTestEndpoints();
        app.MapPassportEndpoints();
        return app;
    }
}
