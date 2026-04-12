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
                                     !string.IsNullOrWhiteSpace(windyConfig.Value.StationPassword) &&
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

        ArgumentNullException.ThrowIfNull(data);


        var queryBuilder = new QueryBuilder
        {
            { StationIdQueryParam, Uri.EscapeDataString(stationId) },
            { WindSpeedQueryParam, data.WindSpeed.ToString("F1", CultureInfo.InvariantCulture) },
            { WindGustQueryParam, data.WindGust.ToString("F1", CultureInfo.InvariantCulture) },
            { WindDirectionQueryParam, data.WindDirection.ToString() },
            { HumidityQueryParam, data.OutHumidity.ToString("F1", CultureInfo.InvariantCulture) },
            { PressureQueryParam, data.RelativePressure.ToString("F1", CultureInfo.InvariantCulture) },
            { UvIndexQueryParam, data.Uvi.ToString("F1", CultureInfo.InvariantCulture) },
            { SolarRadiationQueryParam, data.LightIntensity.ToString("F1", CultureInfo.InvariantCulture) },
            { HourlyRainfallQueryParam, data.HourlyRainfall.ToString("F1", CultureInfo.InvariantCulture) },
            { TemperatureQueryParam, data.OutTemperature.ToString("F1", CultureInfo.InvariantCulture) }
        };

        return queryBuilder.ToString();
    }
}