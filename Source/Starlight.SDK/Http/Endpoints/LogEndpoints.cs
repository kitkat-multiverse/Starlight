using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Starlight.Common;
using System.Text.Json;

namespace Starlight.SDK.Http.Endpoints;

public static class LogEndpoints
{
    public static void MapLogEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/log", VerboseLog);
        routes.MapPost("/sdk/dataUpload", VerboseLog);
        routes.MapPost("/crash/dataUpload", WarningLog);
    }

    private static IResult VerboseLog(
        [FromBody] dynamic? body,
        [FromServices] ILoggerFactory loggerFactory
    )
    {
        // A static type can't be a generic type argument (CS0718), so
        // ILogger<T> isn't an option here. The factory caches by category,
        // so this lookup is cheap.
        var logger = loggerFactory.CreateLogger(typeof(LogEndpoints).FullName!);

        var serialized = JsonSerializer.Serialize(body, Constants.JsonOptions);
        logger.LogTrace("Client sent verbose log: {Body}", serialized as string);

        return TypedResults.Ok(new { Retcode = 0, Message = "OK" });
    }

    private static IResult WarningLog(
        [FromBody] dynamic? body,
        [FromServices] ILoggerFactory loggerFactory
    )
    {
        // A static type can't be a generic type argument (CS0718), so
        // ILogger<T> isn't an option here. The factory caches by category,
        // so this lookup is cheap.
        var logger = loggerFactory.CreateLogger(typeof(LogEndpoints).FullName!);

        var serialized = JsonSerializer.Serialize(body, Constants.JsonOptions);
        logger.LogWarning("Client sent error log: {Body}", serialized as string);

        return TypedResults.Ok(new { Retcode = 0, Message = "OK" });
    }
}
