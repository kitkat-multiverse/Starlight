using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Starlight.Crypto.Client;
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
    public static IHostApplicationBuilder AddDispatchServer(this IHostApplicationBuilder builder)
    {
        var config = builder.Configuration.GetSection("Dispatch").Get<DispatchConfig>() ?? new DispatchConfig();

        builder.TrySetSigningKeyPath("SDK server", config.RsaSigningKeyPath);

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
}
