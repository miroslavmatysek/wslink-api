using System.Collections.Concurrent;
using WsLink.Common.Contract;
using WsLink.Common.DataProvider;

namespace WsLink.Business.Tests.Fakes;

public sealed record InsertCall(DateTimeOffset Time, WeatherStationData Data, CancellationToken CancellationToken);

public sealed record RangeCall(DateTimeOffset From, DateTimeOffset To, int Limit, CancellationToken CancellationToken,
    TimeSpan? Bucket = null);

/// <summary>
/// Records calls instead of connecting to a database, optionally fails with the given exception.
/// Reads return <see cref="Records"/> (latest = newest by time, range = filtered by time, newest rows within the limit;
/// the aggregated range returns the same rows, it only records the bucket).
/// </summary>
public sealed class FakeWeatherDataProvider(Exception? exception = null, bool isEnabled = true) : IWeatherDataProvider
{
    private readonly ConcurrentQueue<InsertCall> calls = new();
    private readonly ConcurrentQueue<RangeCall> rangeCalls = new();

    public IReadOnlyList<InsertCall> Calls => calls.ToList();

    public IReadOnlyList<RangeCall> RangeCalls => rangeCalls.ToList();

    public List<WeatherRecord> Records { get; } = [];

    public bool IsEnabled { get; } = isEnabled;

    /// <summary>
    /// Runs before every read with the read's cancellation token, e.g. to wait until a timeout cancels it.
    /// </summary>
    public Func<CancellationToken, Task>? BeforeRead { get; init; }

    public static FakeWeatherDataProvider Throwing(Exception exception) => new(exception);

    public Task InsertAsync(DateTimeOffset time, WeatherStationData data, CancellationToken cancellationToken)
    {
        calls.Enqueue(new InsertCall(time, data, cancellationToken));
        return exception is null ? Task.CompletedTask : Task.FromException(exception);
    }

    public async Task<WeatherRecord?> GetLatestAsync(CancellationToken cancellationToken)
    {
        if (BeforeRead is not null)
            await BeforeRead(cancellationToken);
        if (exception is not null)
            throw exception;
        return Records.MaxBy(r => r.Time);
    }

    public Task<IReadOnlyList<WeatherRecord>> GetRangeAsync(DateTimeOffset from, DateTimeOffset to, int limit,
        CancellationToken cancellationToken) =>
        Range(from, to, limit, cancellationToken, bucket: null);

    public Task<IReadOnlyList<WeatherRecord>> GetAggregatedRangeAsync(DateTimeOffset from, DateTimeOffset to,
        TimeSpan bucket, int limit, CancellationToken cancellationToken) =>
        Range(from, to, limit, cancellationToken, bucket);

    private async Task<IReadOnlyList<WeatherRecord>> Range(DateTimeOffset from, DateTimeOffset to, int limit,
        CancellationToken cancellationToken, TimeSpan? bucket)
    {
        rangeCalls.Enqueue(new RangeCall(from, to, limit, cancellationToken, bucket));
        if (BeforeRead is not null)
            await BeforeRead(cancellationToken);
        if (exception is not null)
            throw exception;

        // Like the database query: the newest rows within the limit, ordered by time.
        return Records
            .Where(r => r.Time >= from && r.Time < to)
            .OrderByDescending(r => r.Time)
            .Take(limit)
            .OrderBy(r => r.Time)
            .ToList();
    }
}
