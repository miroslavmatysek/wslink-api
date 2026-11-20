using WsLink.Common.Contract;

namespace WsLink.Common.Service;

public enum WeatherQueryStatus
{
    Ok,

    /// <summary>
    /// Nothing is stored yet.
    /// </summary>
    NotFound,

    /// <summary>
    /// The requested time range is invalid, see <see cref="WeatherQueryResult{T}.Error"/>.
    /// </summary>
    InvalidRange,

    /// <summary>
    /// The database storage is disabled or not configured.
    /// </summary>
    StorageDisabled,

    /// <summary>
    /// The database is not reachable, rejected the query or did not answer in time.
    /// </summary>
    StorageUnavailable,
}

public sealed record WeatherQueryResult<T>(WeatherQueryStatus Status, T? Value = default, string? Error = null);

/// <param name="Records">Measurements ordered by time.</param>
/// <param name="Truncated">True when the range contains more rows than returned (the newest ones are returned).</param>
public sealed record WeatherRange(IReadOnlyList<WeatherRecord> Records, bool Truncated);

/// <summary>
/// Reads stored measurements for the preview API.
/// </summary>
public interface IWeatherHistoryService
{
    Task<WeatherQueryResult<WeatherRecord>> GetLatestAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Measurements with from &lt;= time &lt; to, see <see cref="WeatherRangeQuery"/>.
    /// </summary>
    Task<WeatherQueryResult<WeatherRange>> GetRangeAsync(WeatherRangeQuery query,
        CancellationToken cancellationToken);
}

/// <summary>
/// Time range of <see cref="IWeatherHistoryService.GetRangeAsync"/>.
/// </summary>
/// <param name="From">Start (inclusive). Missing: <paramref name="To"/> minus <paramref name="Hours"/>, or 24 hours.</param>
/// <param name="To">End (exclusive). Missing: the server's now.</param>
/// <param name="Hours">
/// Length of the range ending at <paramref name="To"/>, instead of <paramref name="From"/>. Lets a client ask for
/// "the last N hours" without its own clock, which may differ from the server's clock that stamps the rows.
/// </param>
/// <param name="BucketMinutes">One aggregated record per bucket of that many minutes, or raw rows when missing.</param>
public sealed record WeatherRangeQuery(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int? Hours = null,
    int? BucketMinutes = null);
