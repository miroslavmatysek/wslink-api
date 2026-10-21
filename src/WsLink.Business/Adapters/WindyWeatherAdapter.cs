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
    ILogger<WindyWeatherAdapter> logger) : IWeatherAdapter
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

    private DateTime lastUpdate = DateTime.MinValue;

    public bool IsEnabled { get; } = windyConfig.Value.Enabled &&
                                     !string.IsNullOrWhiteSpace(windyConfig.Value.StationPassword) &&
                                     !string.IsNullOrWhiteSpace(windyConfig.Value.ApiHost) &&
                                     !string.IsNullOrWhiteSpace(windyConfig.Value.StationId);

    public async Task ProcessWeatherDataAsync(WeatherStationData data)
    {
        logger.LogDebug("WindyWeatherAdapter received data [Data: {@Data}]", data);

        if (DateTime.Now.Subtract(lastUpdate) > windyConfig.Value.UpdateInterval)
        {
            // Set before sending, so a failed attempt (unreachable Windy, error response like 429) is not retried
            // on every station post and an overlapping station post does not send a second update.
            lastUpdate = DateTime.Now;
            try
            {
                var urlBuilder =
                    new UriBuilder(windyConfig.Value.ApiHost)
                    {
                        Path = DataPath, Query = CreateUpdateDataQuery(windyConfig.Value.StationId, data)
                    };

                logger.LogDebug("Sending Windy weather data [Url: {Url}]", urlBuilder.Uri);

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
        else
        {
            logger.LogDebug("Skipping Windy weather data processing due to recent update");
        }
    }

    private static string CreateUpdateDataQuery(string? stationId, WeatherStationData data)
    {
        if (string.IsNullOrWhiteSpace(stationId))
            throw new ArgumentNullException(nameof(stationId));

        ArgumentNullException.ThrowIfNull(data);


        var queryBuilder = new QueryBuilder { { StationIdQueryParam, stationId } };
        AddIfHasValue(queryBuilder, WindSpeedQueryParam, data.WindSpeed);
        AddIfHasValue(queryBuilder, WindGustQueryParam, data.WindGust);
        AddIfHasValue(queryBuilder, WindDirectionQueryParam, data.WindDirection);
        AddIfHasValue(queryBuilder, HumidityQueryParam, data.OutHumidity);
        AddIfHasValue(queryBuilder, PressureQueryParam, data.RelativePressure);
        AddIfHasValue(queryBuilder, UvIndexQueryParam, data.Uvi);
        AddIfHasValue(queryBuilder, SolarRadiationQueryParam, data.LightIntensity);
        AddIfHasValue(queryBuilder, HourlyRainfallQueryParam, data.HourlyRainfall);
        AddIfHasValue(queryBuilder, TemperatureQueryParam, data.OutTemperature);

        return queryBuilder.ToString();
    }

    private static void AddIfHasValue(QueryBuilder queryBuilder, string name, double? value)
    {
        if (value.HasValue)
            queryBuilder.Add(name, value.Value.ToString("F1", CultureInfo.InvariantCulture));
    }

    private static void AddIfHasValue(QueryBuilder queryBuilder, string name, int? value)
    {
        if (value.HasValue)
            queryBuilder.Add(name, value.Value.ToString(CultureInfo.InvariantCulture));
    }
}