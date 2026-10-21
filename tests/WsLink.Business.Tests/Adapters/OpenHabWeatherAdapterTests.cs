using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WsLink.Business.Adapters;
using WsLink.Business.Tests.Fakes;
using WsLink.Common.Config;
using WsLink.Common.Contract;

namespace WsLink.Business.Tests.Adapters;

public class OpenHabWeatherAdapterTests
{
    private const string BaseUrl = "http://openhab.local:8080";

    private readonly ListLogger<OpenHabWeatherAdapter> logger = new();

    private OpenHabWeatherAdapter CreateAdapter(OpenHabConfig config, FakeHttpMessageHandler? handler = null) =>
        new(new FakeHttpClientFactory(handler ?? FakeHttpMessageHandler.Returning(HttpStatusCode.OK)),
            Options.Create(config), logger);

    private static OpenHabConfig CreateConfig(params (string Property, string Item)[] items)
    {
        var config = new OpenHabConfig { Enabled = true, BaseUrl = BaseUrl };
        foreach (var (property, item) in items)
            config.Items[property] = item;
        return config;
    }

    private static WeatherStationData CreateFullData() => new()
    {
        OutSensorConnected = true,
        OutTemperature = -3.5,
        OutHumidity = 45,
        InTemperature = 22.1,
        InHumidity = 40.5,
        AbsolutePressure = 978.3,
        RelativePressure = 1013.2,
        WindDirection = 270,
        WindSpeed = 3.1,
        WindSpeed10MinutesAvg = 2.7,
        WindGust = 7.3,
        RainRate = 1.2,
        HourlyRainfall = 0.4,
        DailyRainfall = 5.6,
        WeeklyRainfall = 12.3,
        MonthlyRainfall = 45.6,
        YearlyRainfall = 456.7,
        Uvi = 3,
        LightIntensity = 512.3,
    };

    // Item name is the second to last segment of ".../rest/items/{item}/state".
    private static Dictionary<string, string?> StatesByItem(FakeHttpMessageHandler handler) =>
        handler.Requests.ToDictionary(r => r.Uri.Segments[^2].TrimEnd('/'), r => r.Content);

    [Fact]
    public void IsEnabled_DisabledInConfig_IsFalse()
    {
        // Arrange
        var config = CreateConfig(("OutTemperature", "Temp"));
        config.Enabled = false;

        // Act
        var adapter = CreateAdapter(config);

        // Assert
        Assert.False(adapter.IsEnabled);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void IsEnabled_NoValidItemMapping_IsFalseAndLogsError()
    {
        // Arrange
        var config = CreateConfig(("OutTemperature", " "));

        // Act
        var adapter = CreateAdapter(config);

        // Assert
        Assert.False(adapter.IsEnabled);
        Assert.Single(logger.AtLevel(LogLevel.Error));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("openhab.local:8080")]
    [InlineData("ftp://openhab.local")]
    public void IsEnabled_InvalidBaseUrl_IsFalseAndLogsError(string? baseUrl)
    {
        // Arrange
        var config = CreateConfig(("OutTemperature", "Temp"));
        config.BaseUrl = baseUrl;

        // Act
        var adapter = CreateAdapter(config);

        // Assert
        Assert.False(adapter.IsEnabled);
        Assert.Contains(logger.AtLevel(LogLevel.Error), e => e.Message.Contains("Invalid openHAB BaseUrl"));
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_IgnoresUnknownPropertiesAndEmptyItemNames()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK);
        var adapter = CreateAdapter(
            CreateConfig(("OutTemperature", "Temp"), ("NoSuchProperty", "Other"), ("InTemperature", " ")), handler);

        // Act
        await adapter.ProcessWeatherDataAsync(CreateFullData());

        // Assert
        Assert.True(adapter.IsEnabled);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(new Uri($"{BaseUrl}/rest/items/Temp/state"), request.Uri);
        Assert.Contains(logger.AtLevel(LogLevel.Warning), e => e.Message.Contains("NoSuchProperty"));
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_NoItemMapping_UsesDefaultItemForEveryProperty()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK);
        var adapter = CreateAdapter(CreateConfig(), handler);

        // Act
        await adapter.ProcessWeatherDataAsync(CreateFullData());

        // Assert
        Assert.Equal(
            [
                "WsLink_AbsolutePressure", "WsLink_DailyRainfall", "WsLink_HourlyRainfall", "WsLink_InHumidity",
                "WsLink_InTemperature", "WsLink_LightIntensity", "WsLink_MonthlyRainfall", "WsLink_OutHumidity",
                "WsLink_OutSensorConnected", "WsLink_OutTemperature", "WsLink_RainRate", "WsLink_RelativePressure",
                "WsLink_Uvi", "WsLink_WeeklyRainfall", "WsLink_WindDirection", "WsLink_WindGust", "WsLink_WindSpeed",
                "WsLink_WindSpeed10MinutesAvg", "WsLink_YearlyRainfall"
            ],
            StatesByItem(handler).Keys.Order(StringComparer.Ordinal));
        Assert.Equal("-3.5 °C", StatesByItem(handler)["WsLink_OutTemperature"]);
        Assert.Contains(logger.AtLevel(LogLevel.Information), e => e.Message.Contains("default mapping"));
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_ItemMapping_ReplacesDefaultMapping()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK);
        var adapter = CreateAdapter(CreateConfig(("OutTemperature", "Garden_Temperature")), handler);

        // Act
        await adapter.ProcessWeatherDataAsync(CreateFullData());

        // Assert
        Assert.Equal("Garden_Temperature", Assert.Single(StatesByItem(handler)).Key);
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_ItemNameWithSurroundingWhitespace_IsTrimmed()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK);
        var adapter = CreateAdapter(CreateConfig(("YearlyRainfall", " WsLink_YearlyRainfall\n")), handler);

        // Act
        await adapter.ProcessWeatherDataAsync(CreateFullData());

        // Assert
        Assert.Equal(new Uri($"{BaseUrl}/rest/items/WsLink_YearlyRainfall/state"), Assert.Single(handler.Requests).Uri);
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_PropertyNameIsCaseInsensitive()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK);
        var adapter = CreateAdapter(CreateConfig(("outtemperature", "Temp")), handler);

        // Act
        await adapter.ProcessWeatherDataAsync(CreateFullData());

        // Assert
        Assert.Equal("-3.5 °C", Assert.Single(handler.Requests).Content);
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_SendsPlainTextPutToItemStateEndpoint()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.Accepted);
        var adapter = CreateAdapter(CreateConfig(("OutTemperature", "WsLink_OutTemperature")), handler);

        // Act
        await adapter.ProcessWeatherDataAsync(CreateFullData());

        // Assert
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal(new Uri($"{BaseUrl}/rest/items/WsLink_OutTemperature/state"), request.Uri);
        Assert.Equal("text/plain", request.ContentType);
        Assert.Null(request.Authorization);
    }

    [Theory]
    [InlineData("http://proxy.local/openhab")]
    [InlineData("http://proxy.local/openhab/")]
    public async Task ProcessWeatherDataAsync_BaseUrlWithSubPath_KeepsSubPath(string baseUrl)
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK);
        var config = CreateConfig(("OutTemperature", "Temp"));
        config.BaseUrl = baseUrl;
        var adapter = CreateAdapter(config, handler);

        // Act
        await adapter.ProcessWeatherDataAsync(CreateFullData());

        // Assert
        Assert.Equal(new Uri("http://proxy.local/openhab/rest/items/Temp/state"),
            Assert.Single(handler.Requests).Uri);
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_ApiToken_IsSentTrimmedAsBearer()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK);
        var config = CreateConfig(("OutTemperature", "Temp"));
        config.ApiToken = " oh.token.123\n";
        var adapter = CreateAdapter(config, handler);

        // Act
        await adapter.ProcessWeatherDataAsync(CreateFullData());

        // Assert
        Assert.Equal("Bearer oh.token.123", Assert.Single(handler.Requests).Authorization);
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_FormatsStatesWithUnitsUsingInvariantCulture()
    {
        // Arrange
        var properties = new[]
        {
            "OutSensorConnected", "OutTemperature", "OutHumidity", "InTemperature", "InHumidity",
            "AbsolutePressure", "RelativePressure", "WindDirection", "WindSpeed", "WindSpeed10MinutesAvg",
            "WindGust", "RainRate", "HourlyRainfall", "DailyRainfall", "WeeklyRainfall", "MonthlyRainfall",
            "YearlyRainfall", "Uvi", "LightIntensity"
        };
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK);
        var adapter = CreateAdapter(CreateConfig(properties.Select(p => (p, "Item_" + p)).ToArray()), handler);

        // Act
        using (new CultureScope("cs-CZ"))
            await adapter.ProcessWeatherDataAsync(CreateFullData());

        // Assert
        Assert.Equal(new Dictionary<string, string?>
        {
            ["Item_OutSensorConnected"] = "ON",
            ["Item_OutTemperature"] = "-3.5 °C",
            ["Item_OutHumidity"] = "45",
            ["Item_InTemperature"] = "22.1 °C",
            ["Item_InHumidity"] = "40.5",
            ["Item_AbsolutePressure"] = "978.3 hPa",
            ["Item_RelativePressure"] = "1013.2 hPa",
            ["Item_WindDirection"] = "270 °",
            ["Item_WindSpeed"] = "3.1 m/s",
            ["Item_WindSpeed10MinutesAvg"] = "2.7 m/s",
            ["Item_WindGust"] = "7.3 m/s",
            ["Item_RainRate"] = "1.2 mm/h",
            ["Item_HourlyRainfall"] = "0.4 mm",
            ["Item_DailyRainfall"] = "5.6 mm",
            ["Item_WeeklyRainfall"] = "12.3 mm",
            ["Item_MonthlyRainfall"] = "45.6 mm",
            ["Item_YearlyRainfall"] = "456.7 mm",
            ["Item_Uvi"] = "3",
            ["Item_LightIntensity"] = "512.3 W/m²",
        }, StatesByItem(handler));
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_MissingValuesAndDisconnectedSensor_SendsUndefAndOff()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Returning(HttpStatusCode.OK);
        var adapter = CreateAdapter(
            CreateConfig(("OutSensorConnected", "Connected"), ("OutTemperature", "Temp"), ("Uvi", "Uv")), handler);

        // Act
        await adapter.ProcessWeatherDataAsync(new WeatherStationData { OutSensorConnected = false });

        // Assert
        var states = StatesByItem(handler);
        Assert.Equal("OFF", states["Connected"]);
        Assert.Equal("UNDEF", states["Temp"]);
        Assert.Equal("UNDEF", states["Uv"]);
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_AllItemsUpdated_LogsInformation()
    {
        // Arrange
        var adapter = CreateAdapter(CreateConfig(("OutTemperature", "Temp"), ("InTemperature", "InTemp")));

        // Act
        await adapter.ProcessWeatherDataAsync(CreateFullData());

        // Assert
        Assert.Contains(logger.AtLevel(LogLevel.Information), e => e.Message.Contains("Count: 2"));
        Assert.Empty(logger.AtLevel(LogLevel.Warning));
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_ItemRejected_LogsSingleWarningWithFirstError()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler((request, _) => Task.FromResult(new HttpResponseMessage(
            request.RequestUri!.AbsolutePath.Contains("Missing") ? HttpStatusCode.NotFound : HttpStatusCode.OK)));
        var adapter = CreateAdapter(CreateConfig(("OutTemperature", "Temp"), ("InTemperature", "Missing")), handler);

        // Act
        await adapter.ProcessWeatherDataAsync(CreateFullData());

        // Assert
        var warning = Assert.Single(logger.AtLevel(LogLevel.Warning));
        Assert.Contains("Failed: 1", warning.Message);
        Assert.Contains("Total: 2", warning.Message);
        Assert.Contains("Missing: status 404 NotFound", warning.Message);
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_OpenHabUnreachable_LogsSingleWarningWithoutStackTrace()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Throwing(new HttpRequestException("Connection refused"));
        var adapter = CreateAdapter(
            CreateConfig(("OutTemperature", "Temp"), ("InTemperature", "InTemp"), ("Uvi", "Uv")), handler);

        // Act
        await adapter.ProcessWeatherDataAsync(CreateFullData());

        // Assert
        var warning = Assert.Single(logger.AtLevel(LogLevel.Warning));
        Assert.Contains("Failed: 3", warning.Message);
        Assert.Contains("Connection refused", warning.Message);
        Assert.Null(warning.Exception);
        Assert.Empty(logger.AtLevel(LogLevel.Error));
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_UnexpectedException_LogsErrorWithException()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Throwing(new InvalidOperationException("Boom"));
        var adapter = CreateAdapter(CreateConfig(("OutTemperature", "Temp")), handler);

        // Act
        await adapter.ProcessWeatherDataAsync(CreateFullData());

        // Assert
        var error = Assert.Single(logger.AtLevel(LogLevel.Error));
        Assert.IsType<InvalidOperationException>(error.Exception);
        Assert.Single(logger.AtLevel(LogLevel.Warning));
    }
}
