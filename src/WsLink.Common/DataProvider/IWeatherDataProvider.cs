using WsLink.Common.Contract;

namespace WsLink.Common.DataProvider;

/// <summary>
/// Stores and reads measurements in a database. Hides the database access (driver, connection string, driver
/// exceptions), so callers do not depend on the database driver and can be tested without a server.
/// All methods throw <see cref="DataProviderException"/> on an expected failure (database unreachable, statement
/// rejected, timeout) and <see cref="OperationCanceledException"/> when the cancellation token was canceled.
/// </summary>
public interface IWeatherDataProvider
{
    /// <summary>
    /// False when the data provider is disabled or its configuration is invalid.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Stores one measurement.
    /// </summary>
    Task InsertAsync(DateTimeOffset time, WeatherStationData data, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the newest measurement, or null when nothing is stored.
    /// </summary>
    Task<WeatherRecord?> GetLatestAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns measurements with <paramref name="from"/> &lt;= time &lt; <paramref name="to"/> ordered by time,
    /// at most <paramref name="limit"/> rows (the newest ones).
    /// </summary>
    Task<IReadOnlyList<WeatherRecord>> GetRangeAsync(DateTimeOffset from, DateTimeOffset to, int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Like <see cref="GetRangeAsync"/>, but one record per time bucket of length <paramref name="bucket"/>
    /// (record time = bucket start). Values are averaged (rounded to one decimal place), wind gust, rain rate and
    /// rainfall use the maximum, wind direction the circular mean, <c>OutSensorConnected</c> is true when the sensor
    /// was connected at least once in the bucket.
    /// </summary>
    Task<IReadOnlyList<WeatherRecord>> GetAggregatedRangeAsync(DateTimeOffset from, DateTimeOffset to,
        TimeSpan bucket, int limit, CancellationToken cancellationToken);
}
