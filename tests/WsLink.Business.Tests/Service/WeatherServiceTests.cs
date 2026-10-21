using WsLink.Business.Service;
using WsLink.Business.Tests.Fakes;
using WsLink.Common.Contract;

namespace WsLink.Business.Tests.Service;

public class WeatherServiceTests
{
    private readonly ListLogger<WeatherService> logger = new();

    private static WsLinkRequestParam CreateRequest(int? outSensorConnection = 1) => new()
    {
        OutSensorConnection = outSensorConnection,
        OutTemperature1 = 21.37f,
        OutHumidity1 = 55f,
        DewPoint1 = -3.62f,
        InTemperature = 23.04f,
        InHumidity = 41.5f,
        AbsoluteAirPressure = 978.26f,
        RelativeAirPressure = 1013.27f,
        WindDirection1 = 270,
        WindSpeed1 = 3.14,
        WindSpeed10MinutesAvg = 2.66,
        WindGust1 = 7.26,
        RainRate = 1.2,
        HourlyRainfall = 0.4,
        DailyRainfall = 5.56,
        WeeklyRainfall = 12.3,
        MonthlyRainfall = 45.6,
        YearlyRainfall = 456.7,
        Uvi = 3,
        LightIntensity = 512.34,
    };

    private WeatherService CreateService(params FakeWeatherAdapter[] adapters) =>
        new(logger, new FakeAdapterFactory(adapters));

    [Fact]
    public async Task SaveWeatherData_OutSensorConnected_MapsAndRoundsAllValues()
    {
        // Arrange
        var adapter = new FakeWeatherAdapter();
        var service = CreateService(adapter);

        // Act
        await service.SaveWeatherData(CreateRequest());

        // Assert
        var payload = Assert.Single(adapter.Received);
        Assert.True(payload.OutSensorConnected);
        Assert.Equal(21.4, payload.OutTemperature);
        Assert.Equal(55, payload.OutHumidity);
        Assert.Equal(-3.6, payload.DewPoint);
        Assert.Equal(23, payload.InTemperature);
        Assert.Equal(41.5, payload.InHumidity);
        Assert.Equal(978.3, payload.AbsolutePressure);
        Assert.Equal(1013.3, payload.RelativePressure);
        Assert.Equal(270, payload.WindDirection);
        Assert.Equal(3.1, payload.WindSpeed);
        Assert.Equal(2.7, payload.WindSpeed10MinutesAvg);
        Assert.Equal(7.3, payload.WindGust);
        Assert.Equal(1.2, payload.RainRate);
        Assert.Equal(0.4, payload.HourlyRainfall);
        Assert.Equal(5.6, payload.DailyRainfall);
        Assert.Equal(12.3, payload.WeeklyRainfall);
        Assert.Equal(45.6, payload.MonthlyRainfall);
        Assert.Equal(456.7, payload.YearlyRainfall);
        Assert.Equal(3, payload.Uvi);
        Assert.Equal(512.3, payload.LightIntensity);
    }

    [Fact]
    public async Task SaveWeatherData_OutSensorDisconnected_DropsOutdoorValuesAndKeepsIndoorValues()
    {
        // Arrange
        var adapter = new FakeWeatherAdapter();
        var service = CreateService(adapter);

        // Act
        await service.SaveWeatherData(CreateRequest(outSensorConnection: 0));

        // Assert
        var payload = Assert.Single(adapter.Received);
        Assert.False(payload.OutSensorConnected);
        Assert.Null(payload.OutTemperature);
        Assert.Null(payload.OutHumidity);
        Assert.Null(payload.DewPoint);
        Assert.Null(payload.WindDirection);
        Assert.Null(payload.WindSpeed);
        Assert.Null(payload.WindSpeed10MinutesAvg);
        Assert.Null(payload.WindGust);
        Assert.Null(payload.RainRate);
        Assert.Null(payload.HourlyRainfall);
        Assert.Null(payload.DailyRainfall);
        Assert.Null(payload.WeeklyRainfall);
        Assert.Null(payload.MonthlyRainfall);
        Assert.Null(payload.YearlyRainfall);
        Assert.Null(payload.Uvi);
        Assert.Null(payload.LightIntensity);

        Assert.Equal(23, payload.InTemperature);
        Assert.Equal(41.5, payload.InHumidity);
        Assert.Equal(978.3, payload.AbsolutePressure);
        Assert.Equal(1013.3, payload.RelativePressure);
    }

    [Fact]
    public async Task SaveWeatherData_OutSensorConnectionMissing_TreatsSensorAsConnected()
    {
        // Arrange
        var adapter = new FakeWeatherAdapter();
        var service = CreateService(adapter);

        // Act
        await service.SaveWeatherData(CreateRequest(outSensorConnection: null));

        // Assert
        var payload = Assert.Single(adapter.Received);
        Assert.True(payload.OutSensorConnected);
        Assert.Equal(21.4, payload.OutTemperature);
    }

    [Fact]
    public async Task SaveWeatherData_NonFiniteValues_AreMissing()
    {
        // Arrange
        var adapter = new FakeWeatherAdapter();
        var service = CreateService(adapter);
        var request = CreateRequest();
        request.InTemperature = float.NaN;
        request.OutTemperature1 = float.PositiveInfinity;
        request.WindSpeed1 = double.NegativeInfinity;

        // Act
        await service.SaveWeatherData(request);

        // Assert
        var payload = Assert.Single(adapter.Received);
        Assert.Null(payload.InTemperature);
        Assert.Null(payload.OutTemperature);
        Assert.Null(payload.WindSpeed);
    }

    [Fact]
    public async Task SaveWeatherData_ValueRoundedToZeroFromBelow_IsNotNegativeZero()
    {
        // Arrange
        var adapter = new FakeWeatherAdapter();
        var service = CreateService(adapter);
        var request = CreateRequest();
        request.OutTemperature1 = -0.04f;

        // Act
        await service.SaveWeatherData(request);

        // Assert
        var payload = Assert.Single(adapter.Received);
        Assert.Equal(0, payload.OutTemperature);
        Assert.False(double.IsNegative(payload.OutTemperature!.Value));
    }

    [Fact]
    public async Task SaveWeatherData_SendsSamePayloadToAllAdapters()
    {
        // Arrange
        var first = new FakeWeatherAdapter();
        var second = new FakeWeatherAdapter();
        var service = CreateService(first, second);

        // Act
        await service.SaveWeatherData(CreateRequest());

        // Assert
        Assert.Same(Assert.Single(first.Received), Assert.Single(second.Received));
    }

    [Fact]
    public async Task SaveWeatherData_ProcessesAdaptersConcurrently()
    {
        // Arrange
        // The first adapter waits until the second one runs, so sequential processing would never finish.
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new FakeWeatherAdapter(process: () => secondStarted.Task);
        var second = new FakeWeatherAdapter(process: () =>
        {
            secondStarted.SetResult();
            return Task.CompletedTask;
        });
        var service = CreateService(first, second);

        // Act
        await service.SaveWeatherData(CreateRequest())
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(first.Received);
        Assert.Single(second.Received);
    }

    [Fact]
    public async Task SaveWeatherData_LogsOutSensorConnectionChangeOnlyOnce()
    {
        // Arrange
        var service = CreateService(new FakeWeatherAdapter());

        // Act
        await service.SaveWeatherData(CreateRequest(outSensorConnection: 0));
        await service.SaveWeatherData(CreateRequest(outSensorConnection: 0));
        await service.SaveWeatherData(CreateRequest(outSensorConnection: 1));

        // Assert
        Assert.Single(logger.Entries, e => e.Message == "Outdoor sensor is not connected, outdoor values are ignored");
        Assert.Single(logger.Entries, e => e.Message == "Outdoor sensor is connected");
    }
}
