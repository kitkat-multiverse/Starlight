using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Starlight.Crypto;
using Starlight.SDK.Services;

namespace Starlight.SDK.Http.Endpoints;

public static class RegionEndpoints
{
    private const string PlainTextContentType = "text/plain; charset=utf-8";
    private const string EmptyRegion = "CAESGE5vdCBGb3VuZCB2ZXJzaW9uIGNvbmZpZw==";
    private const string DefaultHash = "TW9yZSBsb3ZlIGZvciBVQSBQYXRjaCBwbGF5ZXJz";

    public static void MapRegionEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/query_region_list", HandleQueryRegionList);
        routes.MapGet("/query_cur_region/{name}", HandleQueryCurrentRegion);
    }

    // version=OSRELWin3.0.0&lang=1&platform=3&binary=1&time=257&channel_id=1&sub_channel_id=3
    private static IResult HandleQueryRegionList(
        HttpContext httpContext,
        [FromServices] DispatchRegionCache dispatchCache,
        [FromQuery] string? version,
        [FromQuery] string? lang,
        [FromQuery] string? platform,
        [FromQuery] string? binary,
        [FromQuery] string? time,
        [FromQuery(Name = "channel_id")] string? channelId,
        [FromQuery(Name = "sub_channel_id")] string? subChannelId
    ) => Results.Text(dispatchCache.GetRegionList(httpContext), PlainTextContentType);

    private static IResult HandleQueryCurrentRegion(
        HttpContext httpContext,
        string name,
        [FromQuery(Name = "version")] string? version,
        [FromQuery(Name = "key_id")] string? keyId,
        [FromServices] DispatchRsaCrypto? rsa,
        [FromServices] DispatchRegionCache dispatchCache
    )
    {
        if (version is null)
        {
            return Results.Text(EmptyRegion, PlainTextContentType);
        }

        if (dispatchCache.GetRegion(name) is not {} region)
        {
            return Results.NotFound($"Unknown dispatch region '{name}'.");
        }

        var payload = rsa is not null
                      && int.TryParse(keyId, out var id)
                      && rsa.TryEncryptPayload(region, id, out var encrypted) ?
            encrypted :
            Convert.ToBase64String(region);

        var signature = rsa is { CanSign: true } ? rsa.GenerateSignature(region) : DefaultHash;

        return Results.Text($"{{\"content\":\"{payload}\",\"sign\":\"{signature}\"}}", PlainTextContentType);
    }
}
