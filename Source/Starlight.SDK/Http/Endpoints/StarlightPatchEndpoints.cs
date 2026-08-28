using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Starlight.Crypto.Client;

namespace Starlight.SDK.Http.Endpoints;

public static class StarlightPatchEndpoints
{
    public static void MapStarlightPatchEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/starlight/patchConfig", HandlePatchRequest);
    }

    private static Task<IResult> HandlePatchRequest(
        HttpContext httpContext,
        [FromServices] SdkConfig sdkConfig
    )
    {
        var sdkKey = httpContext.RequestServices.GetRequiredService<ClientCrypto>()
            .SdkKey.ToXmlString(includePrivateParameters: false);

        var checkSignKey = httpContext.RequestServices.GetRequiredService<ClientCrypto>()
            .SigningKey?.ExportSubjectPublicKeyInfoPem() ?? "";

        var response = new {
            sdkKey,
            checkSignKey,
            useSdkRsa = !sdkConfig.MaPassport.Login.SkipRsaDecryption
        };

        return Task.FromResult(Results.Ok(response));
    }
}
