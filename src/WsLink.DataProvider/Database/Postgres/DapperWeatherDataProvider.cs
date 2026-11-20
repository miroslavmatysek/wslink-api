using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Net.Sockets;
using WsLink.Common.Config;
using WsLink.Common.Contract;
using WsLink.Common.DataProvider;

namespace WsLink.DataProvider.Database.Postgres;

public sealed class DapperWeatherDataProvider : IWeatherDataProvider
{
    // Column names are the JsonPropertyName of WeatherStationData (the Home Assistant payload names),
    // so the same names are used in every target. Must match scripts/database/timescaledb.sql.
    internal const string InsertSql =
        """
        INSERT INTO weather_data (
            time, out_sensor_connected, out_temp, out_hum, dew_point, in_temp, in_hum, a_pressure, r_pressure,
            wind_direction, wind_speed, wind_speed_10min, wind_gust, rain_rate, rainfall_hourly, rainfall_daily,
            rainfall_weekly, rainfall_monthly, rainfall_yearly, uvi, light_intensity)
        VALUES (
            @Time, @OutSensorConnected, @OutTemperature, @OutHumidity, @DewPoint, @InTemperature, @InHumidity,
            @AbsolutePressure, @RelativePressure, @WindDirection, @WindSpeed, @WindSpeed10MinutesAvg, @WindGust,
            @RainRate, @HourlyRainfall, @DailyRainfall, @WeeklyRainfall, @MonthlyRainfall, @YearlyRainfall, @Uvi,
            @LightIntensity)
        """;

    private readonly string? connectionString;

    public DapperWeatherDataProvider(IOptions<PostgresConfig> options, ILogger<DapperWeatherDataProvider> logger)
    {
        connectionString = CreateConnectionString(options.Value, logger);
    }

    public bool IsEnabled { get => connectionString is not null; }

    // NpgsqlConnection is pooled per connection string, so opening a connection per upload is cheap.
    public async Task InsertAsync(DateTimeOffset time, WeatherStationData data, CancellationToken cancellationToken)
    {
        if (connectionString is null)
            throw new InvalidOperationException("Postgres data provider is not enabled");

        var parameters = new DynamicParameters(data);
        parameters.Add("Time", time);

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await connection.ExecuteAsync(new CommandDefinition(InsertSql, parameters,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception e) when (e is NpgsqlException or SocketException or IOException or TimeoutException)
        {
            // Server not reachable or the insert rejected (bad credentials, missing table).
            // PostgresException is an NpgsqlException. Cancellation is not wrapped, it is not a database failure.
            throw new DataProviderException(e.Message, e);
        }
    }

    // The connection string contains the password, so it is never logged, only the host and database.
    private static string? CreateConnectionString(PostgresConfig config, ILogger logger)
    {
        if (!config.Enabled)
            return null;

        // A connection string from env variable or docker secret can contain a trailing new line.
        var value = config.ConnectionString?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            logger.LogError("Postgres connection string is not set, Postgres adapter is disabled");
            return null;
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(value);
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            // The message is not logged, it can contain a part of the password: an unquoted password with ';'
            // is split and its rest reported as an unknown keyword ("Couldn't set <part of password>").
            logger.LogError(
                "Invalid Postgres connection string, Postgres adapter is disabled (quote values containing ';' or '=')");
            return null;
        }

        if (string.IsNullOrWhiteSpace(builder.Host) || string.IsNullOrWhiteSpace(builder.Database))
        {
            logger.LogError(
                "Postgres connection string has no Host or Database, Postgres adapter is disabled [Host: {Host}, Database: {Database}]",
                builder.Host, builder.Database);
            return null;
        }

        return value;
    }
}
