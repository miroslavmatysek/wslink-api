using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WsLink.Business.Adapters;
using WsLink.Business.Tests.Fakes;
using WsLink.Common.Config;
using WsLink.Common.Contract;

namespace WsLink.Business.Tests.Adapters;

public class HomeAssistantWeatherAdapterTests
{
    private readonly ListLogger<HomeAssistantWeatherAdapter> logger = new();

    private static HomeAssistantConfig CreateConfig() => new()
    {
        Enabled = true, BaseUrl = "http://ha.local:8123", WebhookId = "webhook-1"
    };

    private HomeAssistantWeatherAdapter CreateAdapter(FakeHttpMessageHandler handler, HomeAssistantConfig? config = null) =>
        new(new FakeHttpClientFactory(handler), Options.Create(config ?? CreateConfig()), logger);

    [Fact]
    public void IsEnabled_CompleteConfig_IsTrue()
    {
        // Arrange
        var adapter = CreateAdapter(FakeHttpMessageHandler.Returning(HttpStatusCode.OK));

        // Act
        var isEnabled = adapter.IsEnabled;

        // Assert
        Assert.True(isEnabled);
    }

    [Fact]
    public void IsEnabled_DisabledInConfig_IsFalse()
    {
        // Arrange
        var config = CreateConfig();
        config.Enabled = false;
        var adapter = CreateAdapter(FakeHttpMessageHandler.Returning(HttpStatusCode.OK), config);

        // Act
        var isEnabled = adapter.IsEnabled;

        // Assert
        Assert.False(isEnabled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void IsEnabled_MissingWebhookId_IsFalse(string? webhookId)
    {
        // Arrange
        var config = CreateConfig();
        config.WebhookId = webhookId;
        var adapter = CreateAdapter(FakeHttpMessageHandler.Returning(HttpStatusCode.OK), config);

        // Act
        var isEnabled = adapter.IsEnabled;

        // Assert
        Assert.False(isEnabled);
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_PostsJsonToWebhookWithoutMissingValues()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK);
        var adapter = CreateAdapter(handler);
        var data = new WeatherStationData
        {
            OutSensorConnected = true, OutTemperature = -3.5, InHumidity = 40.5, WindDirection = 270
        };

        // Act
        await adapter.ProcessWeatherDataAsync(data);

        // Assert
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("http://ha.local:8123/api/webhook/webhook-1"), request.Uri);
        Assert.Equal("application/json", request.ContentType);

        var json = JsonDocument.Parse(request.Content!).RootElement;
        Assert.Equal(["out_sensor_connected", "out_temp", "in_hum", "wind_direction"],
            json.EnumerateObject().Select(p => p.Name));
        Assert.True(json.GetProperty("out_sensor_connected").GetBoolean());
        Assert.Equal(-3.5, json.GetProperty("out_temp").GetDouble());
        Assert.Equal(40.5, json.GetProperty("in_hum").GetDouble());
        Assert.Equal(270, json.GetProperty("wind_direction").GetInt32());
    }

    // The property names are the contract with Home Assistant templates, a rename would silently break them.
    [Fact]
    public async Task ProcessWeatherDataAsync_AllValues_UsesContractPropertyNames()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK);
        var adapter = CreateAdapter(handler);
        var data = new WeatherStationData
        {
            OutSensorConnected = true, OutTemperature = 1, OutHumidity = 1, DewPoint = 1, InTemperature = 1,
            InHumidity = 1, AbsolutePressure = 1, RelativePressure = 1, WindDirection = 1, WindSpeed = 1,
            WindSpeed10MinutesAvg = 1, WindGust = 1, RainRate = 1, HourlyRainfall = 1, DailyRainfall = 1,
            WeeklyRainfall = 1, MonthlyRainfall = 1, YearlyRainfall = 1, Uvi = 1, LightIntensity = 1
        };

        // Act
        await adapter.ProcessWeatherDataAsync(data);

        // Assert
        var json = JsonDocument.Parse(Assert.Single(handler.Requests).Content!).RootElement;
        Assert.Equal(
            [
                "a_pressure", "dew_point", "in_hum", "in_temp", "light_intensity", "out_hum", "out_sensor_connected",
                "out_temp", "r_pressure", "rain_rate", "rainfall_daily", "rainfall_hourly", "rainfall_monthly",
                "rainfall_weekly", "rainfall_yearly", "uvi", "wind_direction", "wind_gust", "wind_speed",
                "wind_speed_10min"
            ],
            json.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_NoBaseUrl_UsesDefaultHomeAssistantUrl()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK);
        var config = CreateConfig();
        config.BaseUrl = null;
        var adapter = CreateAdapter(handler, config);

        // Act
        await adapter.ProcessWeatherDataAsync(new WeatherStationData());

        // Assert
        Assert.Equal(new Uri("http://homeassistant.local:8123/api/webhook/webhook-1"),
            Assert.Single(handler.Requests).Uri);
    }

    [Theory]
    [InlineData("http://ha.local:8123", "http://ha.local:8123/api/webhook/webhook-1")]
    [InlineData("http://ha.local:8123/", "http://ha.local:8123/api/webhook/webhook-1")]
    [InlineData("http://proxy.local/ha", "http://proxy.local/ha/api/webhook/webhook-1")]
    [InlineData("http://proxy.local/ha/", "http://proxy.local/ha/api/webhook/webhook-1")]
    [InlineData("192.168.1.10:8123", "http://192.168.1.10:8123/api/webhook/webhook-1")]
    public async Task ProcessWeatherDataAsync_BuildsWebhookUrlFromBaseUrl(string baseUrl, string expectedUrl)
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK);
        var config = CreateConfig();
        config.BaseUrl = baseUrl;
        var adapter = CreateAdapter(handler, config);

        // Act
        await adapter.ProcessWeatherDataAsync(new WeatherStationData());

        // Assert
        Assert.Equal(new Uri(expectedUrl), Assert.Single(handler.Requests).Uri);
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_Success_LogsInformation()
    {
        // Arrange
        var adapter = CreateAdapter(FakeHttpMessageHandler.Returning(HttpStatusCode.OK));

        // Act
        await adapter.ProcessWeatherDataAsync(new WeatherStationData());

        // Assert
        Assert.Single(logger.AtLevel(LogLevel.Information));
        Assert.Empty(logger.AtLevel(LogLevel.Warning));
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_ErrorStatus_LogsWarning()
    {
        // Arrange
        var adapter = CreateAdapter(FakeHttpMessageHandler.Returning(HttpStatusCode.InternalServerError));

        // Act
        await adapter.ProcessWeatherDataAsync(new WeatherStationData());

        // Assert
        Assert.Contains("InternalServerError", Assert.Single(logger.AtLevel(LogLevel.Warning)).Message);
        Assert.Empty(logger.AtLevel(LogLevel.Information));
    }

    public static TheoryData<Exception> ExpectedFailures =>
    [
        new HttpRequestException("Connection refused"),
        new TaskCanceledException("Timeout"),
    ];

    [Theory]
    [MemberData(nameof(ExpectedFailures))]
    public async Task ProcessWeatherDataAsync_Unreachable_LogsWarningWithoutStackTrace(Exception exception)
    {
        // Arrange
        var adapter = CreateAdapter(FakeHttpMessageHandler.Throwing(exception));

        // Act
        await adapter.ProcessWeatherDataAsync(new WeatherStationData());

        // Assert
        var warning = Assert.Single(logger.AtLevel(LogLevel.Warning));
        Assert.Contains(exception.Message, warning.Message);
        Assert.Null(warning.Exception);
        Assert.Empty(logger.AtLevel(LogLevel.Error));
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_UnexpectedException_LogsErrorWithException()
    {
        // Arrange
        var adapter = CreateAdapter(FakeHttpMessageHandler.Throwing(new InvalidOperationException("Boom")));

        // Act
        await adapter.ProcessWeatherDataAsync(new WeatherStationData());

        // Assert
        Assert.IsType<InvalidOperationException>(Assert.Single(logger.AtLevel(LogLevel.Error)).Exception);
    }
}
