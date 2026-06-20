using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Starlight.Common;
using Starlight.SDK.Common;
using Starlight.SDK.Http.Models;

namespace Starlight.SDK.Http.Endpoints;

/// <summary>
/// Implements <c>POST /data_abtest_api/config/experiment/list</c>.
/// The client polls this endpoint at startup and on scene transitions
/// to fetch the active A/B test configurations for the given scene.
/// </summary>
public static class AbTestEndpoints
{
    public static void MapAbTestEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/data_abtest_api/config/experiment/list", HandleExperimentList);
    }

    private static IResult HandleExperimentList(
        [FromBody] ExperimentListRequest? body,
        [FromServices] SdkConfig sdkConfig
    )
    {
        if (body is null || string.IsNullOrEmpty(body.SceneId))
        {
            return Results.Ok(new ExperimentListResponse {
                Retcode = (int)Retcode.Fail,
                Success = false,
                Message = ExperimentListMessages.MissingSceneId,
                Data = new List<ExperimentData>()
            });
        }

        var data = sdkConfig.AbTest.ReturnEmptyList ?
            new List<ExperimentData>() :
            sdkConfig.AbTest.Experiments
                .Select(e => new ExperimentData {
                    Code = e.Code,
                    Type = e.Type,
                    ConfigId = e.ConfigId,
                    PeriodId = e.PeriodId,
                    Version = e.Version,
                    Configs = e.Configs ?? new Dictionary<string, string>(),
                    SceneWhiteList = e.SceneWhiteList,
                    ExperimentWhiteList = e.ExperimentWhiteList
                })
                .ToList();

        return Results.Ok(new ExperimentListResponse {
            Retcode = (int)Retcode.Success,
            Success = true,
            Message = string.Empty,
            Data = data
        });
    }

    private static class ExperimentListMessages
    {
        public const string MissingSceneId = "Parameter error";
    }
}
