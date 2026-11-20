using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using WsLink.Business.Adapters;
using WsLink.Business.Tests.Fakes;
using WsLink.Common.Contract;
using WsLink.Common.DataProvider;

namespace WsLink.Business.Tests.Adapters;

public class PostgresWeatherAdapterTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 30, 0, TimeSpan.Zero);

    private readonly ListLogger<PostgresWeatherAdapter> logger = new();

    private PostgresWeatherAdapter CreateAdapter(FakeWeatherDataProvider? dataProvider = null) =>
        new(dataProvider ?? new FakeWeatherDataProvider(), new FakeTimeProvider(Now), logger);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsEnabled_DependsOnDataProvider(bool dataProviderEnabled)
    {
        // Arrange
        var adapter = CreateAdapter(new FakeWeatherDataProvider(isEnabled: dataProviderEnabled));

        // Act
        var isEnabled = adapter.IsEnabled;

        // Assert
        isEnabled.Should().Be(dataProviderEnabled);
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_Enabled_InsertsDataWithCurrentUtcTime()
    {
        // Arrange
        var dataProvider = new FakeWeatherDataProvider();
        var adapter = CreateAdapter(dataProvider);
        var data = new WeatherStationData { OutSensorConnected = true, OutTemperature = -3.5 };

        // Act
        await adapter.ProcessWeatherDataAsync(data);

        // Assert
        var call = dataProvider.Calls.Should().ContainSingle().Which;
        call.Time.Should().Be(Now);
        call.Time.Offset.Should().Be(TimeSpan.Zero);
        call.Data.Should().BeSameAs(data);
        call.CancellationToken.CanBeCanceled.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_Success_LogsInformation()
    {
        // Arrange
        var adapter = CreateAdapter();

        // Act
        await adapter.ProcessWeatherDataAsync(new WeatherStationData());

        // Assert
        logger.AtLevel(LogLevel.Information).Should().ContainSingle();
        logger.AtLevel(LogLevel.Warning).Should().BeEmpty();
        logger.AtLevel(LogLevel.Error).Should().BeEmpty();
    }

    public static TheoryData<Exception> ExpectedFailures() =>
    [
        new DataProviderException("Failed to connect to postgres:5432", new IOException("Connection refused")),
        new OperationCanceledException(),
    ];

    [Theory]
    [MemberData(nameof(ExpectedFailures))]
    public async Task ProcessWeatherDataAsync_ExpectedFailure_LogsOneWarningWithoutException(Exception exception)
    {
        // Arrange
        var adapter = CreateAdapter(FakeWeatherDataProvider.Throwing(exception));

        // Act
        await adapter.ProcessWeatherDataAsync(new WeatherStationData());

        // Assert
        var warning = logger.AtLevel(LogLevel.Warning).Should().ContainSingle().Which;
        warning.Exception.Should().BeNull();
        warning.Message.Should().Contain(exception.Message);
        logger.AtLevel(LogLevel.Error).Should().BeEmpty();
        logger.AtLevel(LogLevel.Information).Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_DataProviderException_LogsInnermostCause()
    {
        // Arrange
        var exception = new DataProviderException("Failed to connect to postgres:5432",
            new InvalidOperationException("Npgsql wrapper", new IOException("Connection refused")));
        var adapter = CreateAdapter(FakeWeatherDataProvider.Throwing(exception));

        // Act
        await adapter.ProcessWeatherDataAsync(new WeatherStationData());

        // Assert
        logger.AtLevel(LogLevel.Warning).Should().ContainSingle()
            .Which.Message.Should().Contain("Failed to connect to postgres:5432")
            .And.Contain("Cause: Connection refused");
    }

    [Fact]
    public async Task ProcessWeatherDataAsync_UnexpectedException_LogsErrorWithException()
    {
        // Arrange
        var exception = new InvalidOperationException("boom");
        var adapter = CreateAdapter(FakeWeatherDataProvider.Throwing(exception));

        // Act
        await adapter.ProcessWeatherDataAsync(new WeatherStationData());

        // Assert
        logger.AtLevel(LogLevel.Error).Should().ContainSingle()
            .Which.Exception.Should().BeSameAs(exception);
        logger.AtLevel(LogLevel.Warning).Should().BeEmpty();
    }
}
