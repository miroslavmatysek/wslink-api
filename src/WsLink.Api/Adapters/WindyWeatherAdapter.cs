using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Options;
using WsLink.Api.Common.Adapters;
using WsLink.Api.Common.Config;
using WsLink.Api.Contract;

namespace WsLink.Api.Adapters;

public class WindyWeatherAdapter(
    IHttpClientFactory httpClientFactory,
    IOptions<WindyConfig> windyConfig,
    ILogger<WindyWeatherAdapter> logger) : IWeatherAdapter
{
    private const string HttpClientName = "Windy";
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
                                     string.IsNullOrWhiteSpace(windyConfig.Value.StationPassword) &&
                                     !string.IsNullOrWhiteSpace(windyConfig.Value.ApiHost) &&
                                     !string.IsNullOrWhiteSpace(windyConfig.Value.StationId);

    public async Task ProcessWeatherDataAsync(WeatherStationData data)
    {
        logger.LogDebug("WindyWeatherAdapter received data [Data: {@Data}]", data);

        if (DateTime.Now.Subtract(lastUpdate) > windyConfig.Value.UpdateInterval)
        {
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

                lastUpdate = DateTime.Now;
                logger.LogInformation("Processing Windy weather data [Data: {@Data}, Status: {StatusCode}]", data,
                    response.StatusCode);
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

        if (data is null)
            throw new ArgumentNullException(nameof(data));


        var queryBuilder = new QueryBuilder
        {
            { StationIdQueryParam, Uri.EscapeDataString(stationId) },
            { WindSpeedQueryParam, data.WindSpeed.ToString("F1") },
            { WindGustQueryParam, data.WindGust.ToString("F1") },
            { WindDirectionQueryParam, data.WindDirection.ToString() },
            { HumidityQueryParam, data.OutHumidity.ToString("F1") },
            { PressureQueryParam, data.RelativePressure.ToString("F1") },
            { UvIndexQueryParam, data.Uvi.ToString("F1") },
            { SolarRadiationQueryParam, data.LightIntensity.ToString("F1") },
            { HourlyRainfallQueryParam, data.HourlyRainfall.ToString("F1") },
            { TemperatureQueryParam, data.InTemperature.ToString("F1") }
        };

        return queryBuilder.ToString();
    }
}