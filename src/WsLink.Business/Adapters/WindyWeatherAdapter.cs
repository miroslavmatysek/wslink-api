using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using WsLink.Common.Adapters;
using WsLink.Common.Config;
using WsLink.Common.Contract;

namespace WsLink.Business.Adapters;

public class WindyWeatherAdapter(
    IHttpClientFactory httpClientFactory,
    IOptions<WindyConfig> windyConfig,
    ILogger<WindyWeatherAdapter> logger,
    TimeProvider timeProvider) : IWeatherAdapter
{
    internal const string HttpClientName = "Windy";
    private const string DataPath = "/api/v2/observation/update";

    private const string StationIdQueryParam = "id";
    private const string WindSpeedQueryParam = "wind";
    private const string WindGustQueryParam = "gust";
    private const string WindDirectionQueryParam = "winddir";
    private const string HumidityQueryParam = "humidity";
    private const string PressureQueryParam = "mbar";
    private const string UvIndexQueryParam = "uv";
    private const string SolarRadiationQueryParam = "solarradiation";
    private const string HourlyRainfallQueryParam = "precip";
    private const string TemperatureQueryParam = "temp";
    private const string DewPointQueryParam = "dewpoint";

    // Station id is a secret (anyone with it and the password could upload data), so it is not logged.
    private const string MaskedStationId = "masked";

    // UTC ticks of the last attempted update. A long, so it can be read and set atomically by overlapping posts.
    private long lastUpdateUtcTicks;

    public bool IsEnabled { get; } = windyConfig.Value.Enabled &&
                                     !string.IsNullOrWhiteSpace(windyConfig.Value.StationPassword) &&
                                     !string.IsNullOrWhiteSpace(windyConfig.Value.ApiHost) &&
                                     !string.IsNullOrWhiteSpace(windyConfig.Value.StationId);

    public async Task ProcessWeatherDataAsync(WeatherStationData data)
    {
        logger.LogDebug("WindyWeatherAdapter received data [Data: {@Data}]", data);

        if (!TryStartUpdate())
        {
            logger.LogDebug("Skipping Windy weather data processing due to recent update");
            return;
        }

        try
        {
            var observation = CreateObservation(data);
            var urlBuilder =
                new UriBuilder(windyConfig.Value.ApiHost)
                {
                    Path = DataPath, Query = CreateUpdateDataQuery(windyConfig.Value.StationId, observation)
                };

            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Sending Windy weather data [Query: {Query}]",
                    CreateUpdateDataQuery(MaskedStationId, observation));

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, urlBuilder.Uri);
            request.Headers.Add("Authorization", $"Bearer {windyConfig.Value.StationPassword}");
            request.Headers.Add("Accept", "application/json");
            using var response = await client.SendAsync(request).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
                logger.LogInformation("Processing Windy weather data [Data: {@Data}, Status: {StatusCode}]", data,
                    response.StatusCode);
            else
                logger.LogWarning("Windy weather data rejected [Data: {@Data}, Status: {StatusCode}]", data,
                    response.StatusCode);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // Expected when Windy is not reachable, the stack trace would only flood the log.
            logger.LogWarning("Error processing Windy weather data [Error: {Error}]", e.Message);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error processing Windy weather data");
        }
    }

    // The update time is set before sending, so a failed attempt (unreachable Windy, error response like 429) is not
    // retried on every station post. Compare-and-swap lets only one of overlapping station posts send the update.
    // UTC, so a daylight saving time change does not stop updates for an hour or send them too early.
    private bool TryStartUpdate()
    {
        var nowTicks = timeProvider.GetUtcNow().UtcTicks;
        var lastTicks = Interlocked.Read(ref lastUpdateUtcTicks);
        return nowTicks - lastTicks > windyConfig.Value.UpdateInterval.Ticks &&
               Interlocked.CompareExchange(ref lastUpdateUtcTicks, nowTicks, lastTicks) == lastTicks;
    }

    // Built once and used for both the sent and the logged query, so they cannot differ.
    private static List<KeyValuePair<string, string>> CreateObservation(WeatherStationData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var observation = new List<KeyValuePair<string, string>>();
        AddIfHasValue(observation, WindSpeedQueryParam, data.WindSpeed);
        AddIfHasValue(observation, WindGustQueryParam, data.WindGust);
        AddIfHasValue(observation, WindDirectionQueryParam, data.WindDirection);
        AddIfHasValue(observation, HumidityQueryParam, data.OutHumidity);
        AddIfHasValue(observation, PressureQueryParam, data.RelativePressure);
        AddIfHasValue(observation, UvIndexQueryParam, data.Uvi);
        AddIfHasValue(observation, SolarRadiationQueryParam, data.LightIntensity);
        AddIfHasValue(observation, HourlyRainfallQueryParam, data.HourlyRainfall);
        AddIfHasValue(observation, TemperatureQueryParam, data.OutTemperature);
        AddIfHasValue(observation, DewPointQueryParam, data.DewPoint);
        return observation;
    }

    private static string CreateUpdateDataQuery(string? stationId, IEnumerable<KeyValuePair<string, string>> observation)
    {
        if (string.IsNullOrWhiteSpace(stationId))
            throw new ArgumentNullException(nameof(stationId));

        return new QueryBuilder(observation.Prepend(new(StationIdQueryParam, stationId))).ToString();
    }

    private static void AddIfHasValue(List<KeyValuePair<string, string>> observation, string name, double? value)
    {
        if (value.HasValue)
            observation.Add(new(name, value.Value.ToString("F1", CultureInfo.InvariantCulture)));
    }

    private static void AddIfHasValue(List<KeyValuePair<string, string>> observation, string name, int? value)
    {
        if (value.HasValue)
            observation.Add(new(name, value.Value.ToString(CultureInfo.InvariantCulture)));
    }
}
