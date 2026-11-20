using WsLink.Common.Contract;

namespace WsLink.Common.DataProvider;

/// <summary>
/// Stores measurements in a database. Hides the database access (driver, connection string, driver exceptions),
/// so the adapter does not depend on the database driver and can be tested without a server.
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
    /// <exception cref="DataProviderException">Expected failure: database unreachable, insert rejected, timeout.</exception>
    /// <exception cref="OperationCanceledException">The cancellation token was canceled.</exception>
    Task InsertAsync(DateTimeOffset time, WeatherStationData data, CancellationToken cancellationToken);
}
