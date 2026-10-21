using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet.Exceptions;
using WsLink.Business.Adapters;
using WsLink.Business.Tests.Fakes;
using WsLink.Common.Config;
using WsLink.Common.Contract;

namespace WsLink.Business.Tests.Adapters;

public class GladysWeatherAdapterTests
{
    private const string Host = "mqtt.local";
    private const string DeviceId = "mqtt:wslink";

    private readonly ListLogger<GladysWeatherAdapter> logger = new();

    private GladysWeatherAdapter CreateAdapter(GladysConfig config, FakeMqttPublisher? publisher = null) =>
        new(publisher ?? new FakeMqttPublisher(), Options.Create(config), logger);

    private static GladysConfig CreateConfig(params (string Property, string Feature)[] features)
    {
        var config = new GladysConfig { Enabled = true, Host = Host, DeviceExternalId = DeviceId };
        foreach (var (property, feature) in features)
            config.Features[property] = feature;
        return config;
    }

    private static string Topic(string featureId) => $"gladys/master/device/{DeviceId}/feature/{featureId}/state";

    private static WeatherStationData CreateFullData() => new()
    {
        OutSensorConnected = true,
        OutTemperature = -3.5,
        OutHumidity = 45,
        DewPoint = -8.2,
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

    private static Dictionary<string, string> PayloadsByTopic(FakeMqttPublisher publisher) =>
        Assert.Single(publisher.Calls).Messages.ToDictionary(m => m.Topic, m => m.Payload);

    [Fact]
    public void IsEnabled_CompleteConfig_IsTrue()
    {
        // Arrange
        var adapter = CreateAdapter(CreateConfig());

        // Act
        var isEnabled = adapter.IsEnabled;

        // Assert
        Assert.True(isEnabled);
        Assert.Empty(logger.AtLevel(LogLevel.Error));
    }

    [Fact]
    public void IsEnabled_DisabledInConfig_IsFalse()
    {
        // Arrange
        var config = CreateConfig();
        config.Enabled = false;
        var adapter = CreateAdapter(config);

        // Act
        var isEnabled = adapter.IsEnabled;

        // Assert
        Assert.False(isEnabled);
        Assert.Empty(logger.Entries);
    }

    [Theory]
    [InlineData(null, 1883)]
    [InlineData(" ", 1883)]
    [InlineData("mqtt://mqtt.local", 1883)]
    [InlineData("mqtt.local:1883", 1883)]
    [InlineData(Host, 0)]
    [InlineData(Host, 65536)]
    public void IsEnabled_InvalidBroker_IsFalseAndLogsError(string? host, int port)
    {
        // Arrange
        var config = CreateConfig();
        config.Host = host;
        config.Port = port;
        var adapter = CreateAdapter(config);

        // Act
        var isEnabled = adapter.IsEnabled;

        // Assert
        Assert.False(isEnabled);
        Assert.Contains(logger.AtLevel(LogLevel.Error), e => e.Message.Contains("broker"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("wslink")]
    [InlineData("mqtt:")]
    [InlineData("mqtt:ws/link")]
    [InlineData("mqtt:ws#")]
    [InlineData("mqtt:ws link")]
    public void IsEnabled_InvalidDeviceExternalId_IsFalseAndLogsError(string? deviceExternalId)
    {
        // Arrange
        var config = CreateConfig();
        config.DeviceExternalId = deviceExternalId;
        var adapter = CreateAdapter(config);

        // Act
        var isEnabled = adapter.IsEnabled;

        // Assert
        Assert.False(isEnabled);
        Assert.Contains(logger.AtLevel(LogLevel.Error), e => e.Message.Contains("DeviceExternalId"));
    }

    [Fact]
    public void IsEnabled_NoValidFeatureMapping_IsFalseAndLogsError()
    {
        // Arrange
        var config = CreateConfig(("OutTemperature", " "), ("InTemperature", "wslink:in_temp"),
            ("NoSuchProperty", "mqtt:wslink:x"));
        var adapter = CreateAdapter(config);

        // Act
        var isEnabled = adapter.IsEnabled;

        // Assert
        Assert.False(isEnabled);
        Assert.Contains(logger.AtLevel(LogLevel.Error), e => e.Message.Contains("No valid Gladys feature mapping"));
        Assert.Equal(2, logger.AtLevel(LogLevel.Warning).Count);
    }

    [Theory]
    [InlineData("192.168.1.10")]
    [InlineData("::1")]
    public void IsEnabled_IpAddressHost_IsTrue(string host)
    {
        // Arrange
        var config = CreateConfig();
        config.Host = host;
        var adapter = CreateAdapter(config);

        // Act
        var isEnabled = adapter.IsEnabled;

        // Assert
        Assert.True(isEnabled);
    }

    [Fact]
    public void Constructor_PasswordWithoutUsername_LogsWarning()
    {
        // Arrange
        var config = CreateConfig();
        config.Password = "secret";

        // Act
        var adapter = CreateAdapter(config);

        // Assert
        Assert.True(adapter.IsEnabled);
        var warning = Assert.Single(logger.AtLevel(LogLevel.Warning));
        Assert.Contains("without a username", warning.Message);
        Assert.DoesNotContain("secret", warning.Message);
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_SendsBrokerSettingsTrimmed()
    {
        // Arrange
        var config = CreateConfig(("OutTemperature", "mqtt:wslink:t"));
        config.Host = " mqtt.local ";
        config.Port = 1884;
        config.Username = "gladys";
        config.Password = "secret\n";
        var publisher = new FakeMqttPublisher();
        var adapter = CreateAdapter(config, publisher);

        // Act
        await adapter.ProcessWeatherDataAsync(CreateFullData());

        // Assert
        var broker = Assert.Single(publisher.Calls).Broker;
        Assert.Equal(Host, broker.Host);
        Assert.Equal(1884, broker.Port);
        Assert.Equal("gladys", broker.Username);
        Assert.Equal("secret", broker.Password);
        Assert.Equal("wslink-api", broker.ClientId);
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_NoFeatureMapping_UsesDefaultFeatureForEveryProperty()
    {
        // Arrange
        var publisher = new FakeMqttPublisher();
        var adapter = CreateAdapter(CreateConfig(), publisher);

        // Act
        await adapter.ProcessWeatherDataAsync(CreateFullData());

        // Assert
        var payloads = PayloadsByTopic(publisher);
        Assert.Equal(20, payloads.Count);
        Assert.Equal("-3.5", payloads[Topic("mqtt:wslink:out_temp")]);
        Assert.Equal("1", payloads[Topic("mqtt:wslink:out_sensor_connected")]);
        Assert.Equal("2.7", payloads[Topic("mqtt:wslink:wind_speed_10min")]);
        Assert.Contains(logger.AtLevel(LogLevel.Information), e => e.Message.Contains("default mapping"));
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_FeatureMapping_ReplacesDefaultMappingAndIgnoresInvalidEntries()
    {
        // Arrange
        var config = CreateConfig(("outtemperature", " mqtt:garden:temp "), ("NoSuchProperty", "mqtt:wslink:x"),
            ("InTemperature", ""));
        var publisher = new FakeMqttPublisher();
        var adapter = CreateAdapter(config, publisher);

        // Act
        await adapter.ProcessWeatherDataAsync(CreateFullData());

        // Assert
        var message = Assert.Single(Assert.Single(publisher.Calls).Messages);
        Assert.Equal(Topic("mqtt:garden:temp"), message.Topic);
        Assert.Equal("-3.5", message.Payload);
        Assert.Contains(logger.AtLevel(LogLevel.Warning), e => e.Message.Contains("NoSuchProperty"));
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_FormatsNumbersUsingInvariantCulture()
    {
        // Arrange
        var config = CreateConfig(("OutTemperature", "mqtt:t"), ("RelativePressure", "mqtt:p"),
            ("WindDirection", "mqtt:wd"), ("YearlyRainfall", "mqtt:r"));
        var publisher = new FakeMqttPublisher();
        var adapter = CreateAdapter(config, publisher);

        // Act
        using (new CultureScope("cs-CZ"))
            await adapter.ProcessWeatherDataAsync(CreateFullData());

        // Assert
        Assert.Equal(new Dictionary<string, string>
        {
            [Topic("mqtt:t")] = "-3.5",
            [Topic("mqtt:p")] = "1013.2",
            [Topic("mqtt:wd")] = "270",
            [Topic("mqtt:r")] = "456.7",
        }, PayloadsByTopic(publisher));
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_MissingValuesAndDisconnectedSensor_SkipsMissingAndSendsZero()
    {
        // Arrange
        var config = CreateConfig(("OutSensorConnected", "mqtt:c"), ("OutTemperature", "mqtt:t"),
            ("InTemperature", "mqtt:in"));
        var publisher = new FakeMqttPublisher();
        var adapter = CreateAdapter(config, publisher);
        var data = new WeatherStationData { OutSensorConnected = false, InTemperature = 21.4 };

        // Act
        await adapter.ProcessWeatherDataAsync(data);

        // Assert
        Assert.Equal(new Dictionary<string, string>
        {
            [Topic("mqtt:c")] = "0",
            [Topic("mqtt:in")] = "21.4",
        }, PayloadsByTopic(publisher));
        Assert.Contains(logger.AtLevel(LogLevel.Information), e => e.Message.Contains("Skipped: 1"));
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_AllMappedValuesMissing_DoesNotPublish()
    {
        // Arrange
        var publisher = new FakeMqttPublisher();
        var adapter = CreateAdapter(CreateConfig(("OutTemperature", "mqtt:t")), publisher);

        // Act
        await adapter.ProcessWeatherDataAsync(new WeatherStationData());

        // Assert
        Assert.Empty(publisher.Calls);
        Assert.Empty(logger.AtLevel(LogLevel.Warning));
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_Published_LogsInformationWithTimeLimitedToken()
    {
        // Arrange
        var publisher = new FakeMqttPublisher();
        var adapter = CreateAdapter(CreateConfig(("OutTemperature", "mqtt:t"), ("InTemperature", "mqtt:in")),
            publisher);

        // Act
        await adapter.ProcessWeatherDataAsync(CreateFullData());

        // Assert
        Assert.True(Assert.Single(publisher.Calls).CancellationToken.CanBeCanceled);
        Assert.Contains(logger.AtLevel(LogLevel.Information), e => e.Message.Contains("Count: 2"));
        Assert.Empty(logger.AtLevel(LogLevel.Warning));
    }

    public static TheoryData<Exception> ExpectedFailures() =>
    [
        new MqttCommunicationException("Connection refused"),
        new MqttCommunicationTimedOutException(),
        new SocketException((int)SocketError.HostNotFound),
        new IOException("Connection reset by peer"),
        new OperationCanceledException(),
    ];

    [Theory]
    [MemberData(nameof(ExpectedFailures))]
    public async Task ProcessWeatherDataAsync_BrokerUnreachable_LogsSingleWarningWithoutException(Exception failure)
    {
        // Arrange
        var adapter = CreateAdapter(CreateConfig(), FakeMqttPublisher.Throwing(failure));

        // Act
        await adapter.ProcessWeatherDataAsync(CreateFullData());

        // Assert
        var warning = Assert.Single(logger.AtLevel(LogLevel.Warning));
        Assert.Null(warning.Exception);
        Assert.Contains(failure.Message, warning.Message);
        Assert.Empty(logger.AtLevel(LogLevel.Error));
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_UnexpectedException_LogsErrorWithException()
    {
        // Arrange
        var failure = new InvalidOperationException("boom");
        var adapter = CreateAdapter(CreateConfig(), FakeMqttPublisher.Throwing(failure));

        // Act
        await adapter.ProcessWeatherDataAsync(CreateFullData());

        // Assert
        var error = Assert.Single(logger.AtLevel(LogLevel.Error));
        Assert.Same(failure, error.Exception);
    }
}
