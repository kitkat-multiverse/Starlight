using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Starlight.Crypto.Client;
using Starlight.SDK.Http.Models;

namespace Starlight.SDK.Http.Endpoints;

public static class StarlightPatchEndpoints
{
    public static void MapStarlightPatchEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/starlight/patchConfig", HandlePatchRequest);

    private static Task<IResult> HandlePatchRequest(
        [FromServices] SdkConfig sdkConfig,
        [FromServices] ClientCrypto clientCrypto)
    {
        var sdkKey = clientCrypto.SdkKey
            .ToXmlString(includePrivateParameters: false);

        var checkSignKey = clientCrypto.SigningKey?
            .ExportSubjectPublicKeyInfoPem() ?? "";

        var response = new StarlightPatchResponse {
            SdkKey = sdkKey,
            CheckSignKey = checkSignKey,
            UseSdkRsa = !sdkConfig.MaPassport.Login.SkipRsaDecryption
        };

        return Task.FromResult(Results.Ok(response));
    }
}
