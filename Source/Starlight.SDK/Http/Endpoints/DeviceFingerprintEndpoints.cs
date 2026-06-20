using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Starlight.Common;
using Starlight.SDK.Common;
using Starlight.SDK.Http;
using Starlight.SDK.Http.Models;

namespace Starlight.SDK.Http.Endpoints;

/// <summary>
/// Implements the <c>/device-fp/api/getExtList</c> endpoint. The client
/// uses this to fetch per-platform device fingerprint extension fields
/// that get folded into the device_fp sent with later SDK requests.
/// </summary>
public static class DeviceFingerprintEndpoints
{
    public static void MapDeviceFingerprintEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/device-fp/api/getExtList", HandleGetExtList);
    }

    private static IResult HandleGetExtList(
        [FromQuery] int? platform,
        [FromServices] SdkConfig sdkConfig
    )
    {
        if (platform is null
            || !Enum.IsDefined(typeof(PlatformId), platform.Value))
        {
            var err = new DeviceExtListData {
                Code = DeviceExtStatusCode.Forbidden,
                Message = platform is null ? "The passed parameters are incorrect." : "Unsupported platforms",
                ExtList = new List<string>(),
                PkgList = new List<string>(),
                PkgStr = string.Empty
            };
            return Results.Ok(ApiResponse.Ok(err));
        }

        // Look up the per-platform extension table from configuration. When
        // no entry exists for the requested platform we still return a
        // success code with empty lists.
        if (sdkConfig.DeviceFp.Extensions.TryGetValue(platform.Value, out var ext))
        {
            return Results.Ok(ApiResponse.Ok(new DeviceExtListData {
                Code = DeviceExtStatusCode.Ok,
                Msg = "ok",
                ExtList = ext.Ext ?? new List<string>(),
                PkgList = ext.Pkgs ?? new List<string>(),
                PkgStr = ext.PkgStr ?? string.Empty
            }));
        }

        return Results.Ok(ApiResponse.Ok(new DeviceExtListData {
            Code = DeviceExtStatusCode.Ok,
            ExtList = new List<string>(),
            PkgList = new List<string>(),
            PkgStr = string.Empty
        }));
    }
}
