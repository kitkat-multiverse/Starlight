using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Starlight.Crypto;
using Starlight.Rpc;
using Starlight.Rpc.Proto;
using Starlight.SDK.Http.Endpoints;
using Starlight.SDK.Services;

namespace Starlight.SDK;

public sealed class DispatchService(
    RpcTransport rpc,
    DispatchRegionCache regionCache,
    ILogger<DispatchService> logger
) : IHostedService
{
    private readonly HashSet<IDisposable> _subs = [];

    public async Task StartAsync(CancellationToken ct) =>
        _subs.Add(await rpc.Subscribe<GateHeartbeatNotify>(GateSubjects.ServerHeartbeat, OnServerHeartbeat));

    public Task StopAsync(CancellationToken ct)
    {
        foreach (var sub in _subs)
        {
            sub.Dispose();
        }
        return Task.CompletedTask;
    }

    private Task OnServerHeartbeat(GateHeartbeatNotify msg, RpcMessage _)
    {
        try
        {
            regionCache.Update(msg.RegionId, msg.ServerInfo);
        }
        catch (ArgumentException)
        {
            logger.LogWarning("Received server heartbeat from {ServerId} for undefined region {RegionId}.",
                msg.ServerInfo.ServerId, msg.RegionId);
        }
        return Task.CompletedTask;
    }
}

public static partial class ServiceExtensions
{
    private const string ContentKeyResourcePrefix = "Starlight.SDK.Resources.Keys.";
    private const string ContentKeyResourceSuffix = ".pem";

    public static IHostApplicationBuilder AddDispatchServer(this IHostApplicationBuilder builder)
    {
        var config = builder.Configuration.GetSection("Dispatch").Get<DispatchConfig>() ?? new DispatchConfig();

        var encryptKeys = LoadEmbeddedContentKeys();
        var hasSigningKey = !string.IsNullOrWhiteSpace(config.RsaSigningKeyPath);

        if (!hasSigningKey)
        {
            Log.Warning("Dispatch signing key path is not configured; clients will need to bypass the RSA signature check");
        }

        if (encryptKeys.Count == 0)
        {
            Log.Warning("No dispatch content keys were embedded; region payloads will be sent unencrypted");
        }

        if (hasSigningKey || encryptKeys.Count > 0)
        {
            try
            {
                builder.Services.AddSingleton(DispatchRsaCrypto.Create(config.RsaSigningKeyPath, encryptKeys));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load dispatch RSA keys");
            }
        }

        builder.Services
            .AddSingleton(config)
            .AddSingleton<DispatchRegionCache>()
            .AddHostedService<DispatchService>();

        return builder;
    }

    public static IEndpointRouteBuilder MapDispatchServer(this IEndpointRouteBuilder builder)
    {
        builder.MapRegionEndpoints();
        return builder;
    }

    /// <summary>
    /// Loads the content keys embedded as <c>Starlight.SDK.Resources.Keys.{id}.pem</c>,
    /// indexed by the numeric <c>key_id</c> parsed from each resource name.
    /// </summary>
    private static Dictionary<int, string> LoadEmbeddedContentKeys()
    {
        var assembly = typeof(DispatchService).Assembly;
        var keys = new Dictionary<int, string>();

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ContentKeyResourcePrefix, StringComparison.Ordinal)
                || !name.EndsWith(ContentKeyResourceSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            var idText = name[ContentKeyResourcePrefix.Length..^ContentKeyResourceSuffix.Length];

            if (!int.TryParse(idText, out var keyId))
            {
                Log.Warning("Skipping content key resource with non-numeric id: {Resource}", name);
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            keys[keyId] = reader.ReadToEnd();
        }

        return keys;
    }
}
