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

    // Columns are aliased to the WeatherRecord property names, Dapper maps by name.
    internal const string SelectColumns =
        """
        time AS Time, out_sensor_connected AS OutSensorConnected, out_temp AS OutTemperature,
        out_hum AS OutHumidity, dew_point AS DewPoint, in_temp AS InTemperature, in_hum AS InHumidity,
        a_pressure AS AbsolutePressure, r_pressure AS RelativePressure, wind_direction AS WindDirection,
        wind_speed AS WindSpeed, wind_speed_10min AS WindSpeed10MinutesAvg, wind_gust AS WindGust,
        rain_rate AS RainRate, rainfall_hourly AS HourlyRainfall, rainfall_daily AS DailyRainfall,
        rainfall_weekly AS WeeklyRainfall, rainfall_monthly AS MonthlyRainfall, rainfall_yearly AS YearlyRainfall,
        uvi AS Uvi, light_intensity AS LightIntensity
        """;

    // ORDER BY time DESC LIMIT 1 reads only the newest chunk of the hypertable.
    internal const string SelectLatestSql =
        $"SELECT {SelectColumns} FROM weather_data ORDER BY time DESC LIMIT 1";

    // Newest rows first, so a limited result keeps the most recent data (the end of a chart);
    // reversed to ascending order in GetRangeAsync.
    internal const string SelectRangeSql =
        $"SELECT {SelectColumns} FROM weather_data WHERE time >= @From AND time < @To ORDER BY time DESC LIMIT @Limit";

    // One row per bucket, so a week can be charted with a few hundred points instead of tens of thousands of rows.
    // Averages are rounded like the stored values. Gust, rain rate and rainfall use the maximum (a peak or a total
    // must not be smoothed away). Wind direction is a circular mean: the plain average of 359° and 1° would be 180°;
    // it is rounded before the modulo, so 359.7° becomes 0°, never 360°.
    internal const string AggregatedColumns =
        """
        time_bucket(@Bucket, time) AS Time, bool_or(out_sensor_connected) AS OutSensorConnected,
        round(avg(out_temp)::numeric, 1)::float8 AS OutTemperature,
        round(avg(out_hum)::numeric, 1)::float8 AS OutHumidity,
        round(avg(dew_point)::numeric, 1)::float8 AS DewPoint,
        round(avg(in_temp)::numeric, 1)::float8 AS InTemperature,
        round(avg(in_hum)::numeric, 1)::float8 AS InHumidity,
        round(avg(a_pressure)::numeric, 1)::float8 AS AbsolutePressure,
        round(avg(r_pressure)::numeric, 1)::float8 AS RelativePressure,
        mod(round(degrees(atan2(avg(sin(radians(wind_direction))), avg(cos(radians(wind_direction))))))::int
            + 360, 360) AS WindDirection,
        round(avg(wind_speed)::numeric, 1)::float8 AS WindSpeed,
        round(avg(wind_speed_10min)::numeric, 1)::float8 AS WindSpeed10MinutesAvg,
        max(wind_gust) AS WindGust,
        max(rain_rate) AS RainRate,
        max(rainfall_hourly) AS HourlyRainfall,
        max(rainfall_daily) AS DailyRainfall,
        max(rainfall_weekly) AS WeeklyRainfall,
        max(rainfall_monthly) AS MonthlyRainfall,
        max(rainfall_yearly) AS YearlyRainfall,
        round(avg(uvi)::numeric, 1)::float8 AS Uvi,
        round(avg(light_intensity)::numeric, 1)::float8 AS LightIntensity
        """;

    // Newest buckets first like SelectRangeSql, reversed to ascending order in GetAggregatedRangeAsync.
    internal const string SelectAggregatedRangeSql =
        $"SELECT {AggregatedColumns} FROM weather_data WHERE time >= @From AND time < @To " +
        "GROUP BY 1 ORDER BY 1 DESC LIMIT @Limit";

    private readonly string? connectionString;

    public DapperWeatherDataProvider(IOptions<PostgresConfig> options, ILogger<DapperWeatherDataProvider> logger)
    {
        connectionString = CreateConnectionString(options.Value, logger);
    }

    public bool IsEnabled { get => connectionString is not null; }

    public Task InsertAsync(DateTimeOffset time, WeatherStationData data, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters(data);
        parameters.Add("Time", time);

        return ExecuteAsync(connection => connection.ExecuteAsync(new CommandDefinition(InsertSql, parameters,
            cancellationToken: cancellationToken)), cancellationToken);
    }

    public Task<WeatherRecord?> GetLatestAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(connection => connection.QuerySingleOrDefaultAsync<WeatherRecord?>(
            new CommandDefinition(SelectLatestSql, cancellationToken: cancellationToken)), cancellationToken);

    public async Task<IReadOnlyList<WeatherRecord>> GetRangeAsync(DateTimeOffset from, DateTimeOffset to, int limit,
        CancellationToken cancellationToken)
    {
        // Npgsql accepts only UTC DateTimeOffset values for timestamptz.
        var parameters = new { From = from.ToUniversalTime(), To = to.ToUniversalTime(), Limit = limit };
        var records = await ExecuteAsync(connection => connection.QueryAsync<WeatherRecord>(
            new CommandDefinition(SelectRangeSql, parameters, cancellationToken: cancellationToken)),
            cancellationToken).ConfigureAwait(false);
        return NewestFirstToAscending(records.AsList());
    }

    public async Task<IReadOnlyList<WeatherRecord>> GetAggregatedRangeAsync(DateTimeOffset from, DateTimeOffset to,
        TimeSpan bucket, int limit, CancellationToken cancellationToken)
    {
        // TimeSpan is sent as interval, the buckets are aligned in UTC.
        var parameters = new
        {
            From = from.ToUniversalTime(), To = to.ToUniversalTime(), Bucket = bucket, Limit = limit
        };
        var records = await ExecuteAsync(connection => connection.QueryAsync<WeatherRecord>(
            new CommandDefinition(SelectAggregatedRangeSql, parameters, cancellationToken: cancellationToken)),
            cancellationToken).ConfigureAwait(false);
        return NewestFirstToAscending(records.AsList());
    }

    // SelectRangeSql returns the newest rows first (so LIMIT keeps the latest data), callers expect ascending time.
    internal static IReadOnlyList<WeatherRecord> NewestFirstToAscending(List<WeatherRecord> newestFirst)
    {
        newestFirst.Reverse();
        return newestFirst;
    }

    // NpgsqlConnection is pooled per connection string, so opening a connection per call is cheap.
    private async Task<T> ExecuteAsync<T>(Func<NpgsqlConnection, Task<T>> action, CancellationToken cancellationToken)
    {
        if (connectionString is null)
            throw new InvalidOperationException("Postgres data provider is not enabled");

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return await action(connection).ConfigureAwait(false);
        }
        catch (Exception e) when (e is NpgsqlException or SocketException or IOException or TimeoutException)
        {
            // Server not reachable or the statement rejected (bad credentials, missing table or privilege).
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
