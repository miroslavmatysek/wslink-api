using Microsoft.Extensions.Logging;
using WsLink.Common.Contract;
using WsLink.Common.DataProvider;
using WsLink.Common.Service;

namespace WsLink.Business.Service;

public class WeatherHistoryService(
    IWeatherDataProvider dataProvider,
    TimeProvider timeProvider,
    ILogger<WeatherHistoryService> logger) : IWeatherHistoryService
{
    internal static readonly TimeSpan DefaultRange = TimeSpan.FromHours(24);
    internal static readonly TimeSpan MaxRange = TimeSpan.FromDays(7);
    internal static readonly int MaxHours = (int)MaxRange.TotalHours;

    // A week of uploads every 30 s is 20 160 rows, the limit protects the service and the browser
    // against a station uploading more often. Long ranges are meant to be read aggregated (bucket).
    internal const int MaxRecords = 20_000;

    // From one minute (finer than most upload intervals) to one day.
    internal const int MinBucketMinutes = 1;
    internal const int MaxBucketMinutes = 1440;

    // A slow database must not keep the request (and a connection) open for long. Settable for tests.
    internal TimeSpan QueryTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public async Task<WeatherQueryResult<WeatherRecord>> GetLatestAsync(CancellationToken cancellationToken)
    {
        if (!dataProvider.IsEnabled)
            return new(WeatherQueryStatus.StorageDisabled);

        var result = await QueryAsync(token => dataProvider.GetLatestAsync(token), cancellationToken)
            .ConfigureAwait(false);
        if (result.Status != WeatherQueryStatus.Ok)
            return new(result.Status);

        return result.Value is null ? new(WeatherQueryStatus.NotFound) : new(WeatherQueryStatus.Ok, result.Value);
    }

    public async Task<WeatherQueryResult<WeatherRange>> GetRangeAsync(WeatherRangeQuery query,
        CancellationToken cancellationToken)
    {
        if (!dataProvider.IsEnabled)
            return new(WeatherQueryStatus.StorageDisabled);

        if (query.BucketMinutes is < MinBucketMinutes or > MaxBucketMinutes)
            return new(WeatherQueryStatus.InvalidRange,
                Error: $"'bucket' must be between {MinBucketMinutes} and {MaxBucketMinutes} minutes");
        if (query.Hours is not null && query.From is not null)
            return new(WeatherQueryStatus.InvalidRange, Error: "Use either 'from' or 'hours', not both");
        if (query.Hours is < 1 || query.Hours > MaxHours)
            return new(WeatherQueryStatus.InvalidRange, Error: $"'hours' must be between 1 and {MaxHours}");

        // In UTC, because DateTimeOffset arithmetic works on the clock time: with a negative offset the clock time
        // is earlier than the UTC time, so the check below would pass and the subtraction still throw.
        var rangeTo = (query.To ?? timeProvider.GetUtcNow()).ToUniversalTime();
        var length = query.Hours is { } hours ? TimeSpan.FromHours(hours) : DefaultRange;
        // Subtracting the range length from a 'to' near DateTimeOffset.MinValue would throw (HTTP 500).
        if (query.From is null && rangeTo.UtcDateTime < DateTime.MinValue + length)
            return new(WeatherQueryStatus.InvalidRange, Error: "'to' is out of range");
        var rangeFrom = query.From?.ToUniversalTime() ?? rangeTo - length;
        if (rangeFrom >= rangeTo)
            return new(WeatherQueryStatus.InvalidRange, Error: "'from' must be before 'to'");
        if (rangeTo - rangeFrom > MaxRange)
            return new(WeatherQueryStatus.InvalidRange,
                Error: $"The range must not be longer than {MaxRange.TotalDays} days");

        // One row more than returned tells whether the range was truncated. The provider returns the newest rows,
        // so the extra one is the oldest (first) and a truncated chart still ends with the latest data.
        var result = await QueryAsync(token => query.BucketMinutes is { } minutes
                ? dataProvider.GetAggregatedRangeAsync(rangeFrom, rangeTo, TimeSpan.FromMinutes(minutes),
                    MaxRecords + 1, token)
                : dataProvider.GetRangeAsync(rangeFrom, rangeTo, MaxRecords + 1, token),
            cancellationToken).ConfigureAwait(false);
        if (result.Status != WeatherQueryStatus.Ok)
            return new(result.Status);

        var records = result.Value!;
        return records.Count > MaxRecords
            ? new(WeatherQueryStatus.Ok,
                new WeatherRange(records.Skip(records.Count - MaxRecords).ToList(), Truncated: true))
            : new(WeatherQueryStatus.Ok, new WeatherRange(records, Truncated: false));
    }

    private async Task<WeatherQueryResult<T>> QueryAsync<T>(Func<CancellationToken, Task<T>> query,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(QueryTimeout);
        try
        {
            return new(WeatherQueryStatus.Ok, await query(timeoutSource.Token).ConfigureAwait(false));
        }
        catch (Exception e) when (cancellationToken.IsCancellationRequested &&
                                  e is DataProviderException or OperationCanceledException)
        {
            // The request was canceled (client disconnected), not a database failure. Npgsql does not always throw
            // OperationCanceledException then (e.g. a broken stream wrapped in DataProviderException), so the
            // request token decides. ASP.NET Core logs any other exception as an unhandled error, so it is turned
            // into an OperationCanceledException (the original one kept as inner). Other exception types (bugs)
            // are not caught, so they are still logged.
            if (e is OperationCanceledException)
                throw;
            throw new OperationCanceledException(e.Message, e, cancellationToken);
        }
        catch (Exception e) when (e is DataProviderException or OperationCanceledException)
        {
            // The request token is not canceled here. When the timeout fired, Npgsql may still report it as
            // a broken stream (DataProviderException), so the timeout token decides which message is logged.
            if (timeoutSource.IsCancellationRequested)
                logger.LogWarning("Reading weather data from Postgres timed out [Timeout: {Timeout}]", QueryTimeout);
            else
                // Expected when the database is down or rejects the query, the stack trace would only flood the log.
                logger.LogWarning("Error reading weather data from Postgres [Error: {Error}, Cause: {Cause}]",
                    e.Message, e.GetBaseException().Message);
            return new(WeatherQueryStatus.StorageUnavailable);
        }
    }
}
