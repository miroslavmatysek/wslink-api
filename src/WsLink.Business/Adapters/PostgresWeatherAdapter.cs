using Microsoft.Extensions.Logging;
using WsLink.Common.Adapters;
using WsLink.Common.Contract;
using WsLink.Common.DataProvider;

namespace WsLink.Business.Adapters;

/// <summary>
/// Stores every upload as one row of the weather_data table (TimescaleDB hypertable, see docs/sql/timescaledb.sql).
/// The database access and its configuration are in <see cref="IWeatherDataProvider"/> (WsLink.DataProvider),
/// Business does not depend on the database driver.
/// </summary>
public class PostgresWeatherAdapter(
    IWeatherDataProvider dataProvider,
    TimeProvider timeProvider,
    ILogger<PostgresWeatherAdapter> logger) : IWeatherAdapter
{
    // Limit for the whole insert (connect + command), the station request waits until all adapters are processed.
    private static readonly TimeSpan UpdateTimeout = TimeSpan.FromSeconds(10);

    public bool IsEnabled { get => dataProvider.IsEnabled; }

    public async Task ProcessWeatherDataAsync(WeatherStationData data)
    {
        // Time of receipt, the station clock is not reliable and not every station firmware sends its time.
        var time = timeProvider.GetUtcNow();

        using var cancellationTokenSource = new CancellationTokenSource(UpdateTimeout);
        try
        {
            await dataProvider.InsertAsync(time, data, cancellationTokenSource.Token).ConfigureAwait(false);
            logger.LogInformation("Weather data stored in Postgres [Time: {Time}]", time);
        }
        catch (Exception e) when (e is DataProviderException or OperationCanceledException)
        {
            // Expected when the server is not reachable or rejects the insert (bad credentials, missing table),
            // the stack trace would only flood the log. The driver message often names only the target
            // ("Failed to connect to postgres:5432"), the innermost exception has the reason ("Connection refused").
            logger.LogWarning(
                "Error storing weather data in Postgres [Error: {Error}, Cause: {Cause}, Timeout: {Timeout}]",
                e.Message, e.GetBaseException().Message, UpdateTimeout);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error storing weather data in Postgres");
        }
    }
}
