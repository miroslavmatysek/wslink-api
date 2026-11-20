using Microsoft.AspNetCore.Mvc;
using WsLink.Common.Contract;
using WsLink.Common.Service;

namespace WsLink.Api.Controllers;

/// <summary>
/// Read-only preview of the measurements stored in the database.
/// </summary>
[ApiController]
[Route("api/weather")]
public class WeatherDataController(IWeatherHistoryService historyService) : ControllerBase
{
    internal const string TruncatedHeader = "X-Result-Truncated";

    [HttpGet("latest")]
    [ProducesResponseType<WeatherRecord>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<WeatherRecord>> GetLatest(CancellationToken cancellationToken)
    {
        var result = await historyService.GetLatestAsync(cancellationToken);
        return result.Status == WeatherQueryStatus.Ok ? result.Value! : Problem(result);
    }

    /// <param name="from">Start of the range (inclusive, ISO 8601), default 24 hours before <paramref name="to"/>.</param>
    /// <param name="to">End of the range (exclusive, ISO 8601), default now.</param>
    /// <param name="hours">Instead of <paramref name="from"/>: the last N hours before <paramref name="to"/> (1-168).</param>
    /// <param name="bucket">Optional bucket length in minutes (1-1440): one aggregated record per bucket.</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<WeatherRecord>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<WeatherRecord>>> GetRange(
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] int? hours,
        [FromQuery] int? bucket, CancellationToken cancellationToken)
    {
        var result = await historyService.GetRangeAsync(new WeatherRangeQuery(from, to, hours, bucket),
            cancellationToken);
        if (result.Status != WeatherQueryStatus.Ok)
            return Problem(result);

        // A header keeps the body a plain array, which is easy to use in charts and scripts.
        if (result.Value!.Truncated)
            Response.Headers[TruncatedHeader] = "true";
        return Ok(result.Value.Records);
    }

    private ObjectResult Problem<T>(WeatherQueryResult<T> result) => result.Status switch
    {
        WeatherQueryStatus.NotFound => Problem(statusCode: StatusCodes.Status404NotFound,
            detail: "No measurement is stored yet"),
        WeatherQueryStatus.InvalidRange => Problem(statusCode: StatusCodes.Status400BadRequest, detail: result.Error),
        WeatherQueryStatus.StorageDisabled => Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
            detail: "Database storage is not enabled"),
        _ => Problem(statusCode: StatusCodes.Status503ServiceUnavailable, detail: "Database is not available"),
    };
}
