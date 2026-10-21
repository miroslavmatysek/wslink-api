using System.Net;
using System.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WsLink.Business.Adapters;
using WsLink.Business.Tests.Fakes;
using WsLink.Common.Config;
using WsLink.Common.Contract;

namespace WsLink.Business.Tests.Adapters;

public class WindyWeatherAdapterTests
{
    private readonly ListLogger<WindyWeatherAdapter> logger = new();
    private readonly FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));

    private static WindyConfig CreateConfig() => new()
    {
        Enabled = true,
        ApiHost = "https://stations.windy.com",
        StationId = "station-1",
        StationPassword = "secret",
        UpdateInterval = TimeSpan.FromMinutes(5),
    };

    private WindyWeatherAdapter CreateAdapter(FakeHttpMessageHandler handler, WindyConfig? config = null) =>
        new(new FakeHttpClientFactory(handler), Options.Create(config ?? CreateConfig()), logger, timeProvider);

    private static WeatherStationData CreateData() => new()
    {
        OutSensorConnected = true,
        OutTemperature = -3.5,
        OutHumidity = 45,
        DewPoint = -8.2,
        RelativePressure = 1013.2,
        WindDirection = 270,
        WindSpeed = 3.1,
        WindGust = 7.3,
        HourlyRainfall = 0.4,
        Uvi = 3,
        LightIntensity = 512.3,
    };

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

    public static TheoryData<string> IncompleteConfigs => ["disabled", "no password", "no station", "no host"];

    [Theory]
    [MemberData(nameof(IncompleteConfigs))]
    public void IsEnabled_IncompleteConfig_IsFalse(string incompleteConfig)
    {
        // Arrange
        var config = CreateConfig();
        switch (incompleteConfig)
        {
            case "disabled": config.Enabled = false; break;
            case "no password": config.StationPassword = " "; break;
            case "no station": config.StationId = null; break;
            case "no host": config.ApiHost = ""; break;
        }
        var adapter = CreateAdapter(FakeHttpMessageHandler.Returning(HttpStatusCode.OK), config);

        // Act
        var isEnabled = adapter.IsEnabled;

        // Assert
        Assert.False(isEnabled);
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_SendsObservationWithBearerPassword()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK);
        var adapter = CreateAdapter(handler);

        // Act
        using (new CultureScope("cs-CZ"))
            await adapter.ProcessWeatherDataAsync(CreateData());

        // Assert
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://stations.windy.com/api/v2/observation/update", request.Uri.GetLeftPart(UriPartial.Path));
        Assert.Equal("Bearer secret", request.Authorization);

        var query = HttpUtility.ParseQueryString(request.Uri.Query);
        Assert.Equal(new Dictionary<string, string?>
        {
            ["id"] = "station-1",
            ["wind"] = "3.1",
            ["gust"] = "7.3",
            ["winddir"] = "270",
            ["humidity"] = "45.0",
            ["mbar"] = "1013.2",
            ["uv"] = "3.0",
            ["solarradiation"] = "512.3",
            ["precip"] = "0.4",
            ["temp"] = "-3.5",
            ["dewpoint"] = "-8.2",
        }, query.AllKeys.ToDictionary(k => k!, k => query[k]));
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_DoesNotLogStationId()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK);
        var adapter = CreateAdapter(handler);

        // Act
        await adapter.ProcessWeatherDataAsync(CreateData());

        // Assert
        Assert.Contains(logger.Entries, e => e.Message.Contains("temp=-3.5"));
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("station-1"));
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_MissingValues_AreNotSent()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK);
        var adapter = CreateAdapter(handler);

        // Act
        await adapter.ProcessWeatherDataAsync(new WeatherStationData { OutTemperature = 1 });

        // Assert
        var query = HttpUtility.ParseQueryString(Assert.Single(handler.Requests).Uri.Query);
        Assert.Equal(new string?[] { "id", "temp" }, query.AllKeys);
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_WithinUpdateInterval_SkipsUpdate()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK);
        var adapter = CreateAdapter(handler);

        // Act
        await adapter.ProcessWeatherDataAsync(CreateData());
        await adapter.ProcessWeatherDataAsync(CreateData());

        // Assert
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_AfterUpdateInterval_SendsUpdate()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK);
        var adapter = CreateAdapter(handler);

        // Act
        await adapter.ProcessWeatherDataAsync(CreateData());
        timeProvider.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        await adapter.ProcessWeatherDataAsync(CreateData());

        // Assert
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_FailedUpdate_IsNotRetriedWithinUpdateInterval()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.TooManyRequests);
        var adapter = CreateAdapter(handler);

        // Act
        await adapter.ProcessWeatherDataAsync(CreateData());
        await adapter.ProcessWeatherDataAsync(CreateData());

        // Assert
        Assert.Single(handler.Requests);
        Assert.Contains("TooManyRequests", Assert.Single(logger.AtLevel(LogLevel.Warning)).Message);
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_Unreachable_LogsWarningWithoutStackTrace()
    {
        // Arrange
        var adapter = CreateAdapter(FakeHttpMessageHandler.Throwing(new HttpRequestException("Connection refused")));

        // Act
        await adapter.ProcessWeatherDataAsync(CreateData());

        // Assert
        var warning = Assert.Single(logger.AtLevel(LogLevel.Warning));
        Assert.Contains("Connection refused", warning.Message);
        Assert.Null(warning.Exception);
        Assert.Empty(logger.AtLevel(LogLevel.Error));
    }
}
