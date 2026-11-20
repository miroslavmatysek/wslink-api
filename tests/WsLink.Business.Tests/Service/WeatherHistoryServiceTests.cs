using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using WsLink.Business.Service;
using WsLink.Business.Tests.Fakes;
using WsLink.Common.Contract;
using WsLink.Common.DataProvider;
using WsLink.Common.Service;

namespace WsLink.Business.Tests.Service;

public class WeatherHistoryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly ListLogger<WeatherHistoryService> logger = new();

    private WeatherHistoryService CreateService(FakeWeatherDataProvider dataProvider) =>
        new(dataProvider, new FakeTimeProvider(Now), logger);

    private static WeatherRecord CreateRecord(DateTimeOffset time, double outTemperature = 1.5) =>
        new() { Time = time, OutSensorConnected = true, OutTemperature = outTemperature };

    [Fact]
    public async Task GetLatestAsync_StoredRecords_ReturnsNewest()
    {
        // Arrange
        // Added out of order: the newest is chosen by time, like ORDER BY time DESC in the database.
        var dataProvider = new FakeWeatherDataProvider();
        var newest = CreateRecord(Now.AddMinutes(-1));
        dataProvider.Records.Add(newest);
        dataProvider.Records.Add(CreateRecord(Now.AddMinutes(-2)));
        var service = CreateService(dataProvider);

        // Act
        var result = await service.GetLatestAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(WeatherQueryStatus.Ok);
        result.Value.Should().BeSameAs(newest);
    }

    [Fact]
    public async Task GetLatestAsync_NothingStored_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService(new FakeWeatherDataProvider());

        // Act
        var result = await service.GetLatestAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(WeatherQueryStatus.NotFound);
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestAsync_StorageDisabled_ReturnsStorageDisabled()
    {
        // Arrange
        var service = CreateService(new FakeWeatherDataProvider(isEnabled: false));

        // Act
        var result = await service.GetLatestAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(WeatherQueryStatus.StorageDisabled);
    }

    [Fact]
    public async Task GetRangeAsync_NoBounds_QueriesLast24HoursWithLimitPlusOne()
    {
        // Arrange
        var dataProvider = new FakeWeatherDataProvider();
        var service = CreateService(dataProvider);

        // Act
        var result = await service.GetRangeAsync(new WeatherRangeQuery(), TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(WeatherQueryStatus.Ok);
        var call = dataProvider.RangeCalls.Should().ContainSingle().Which;
        call.From.Should().Be(Now.AddHours(-24));
        call.To.Should().Be(Now);
        call.Limit.Should().Be(WeatherHistoryService.MaxRecords + 1);
        call.CancellationToken.CanBeCanceled.Should().BeTrue();
    }

    [Fact]
    public async Task GetRangeAsync_OnlyTo_QueriesDefaultRangeBeforeTo()
    {
        // Arrange
        var dataProvider = new FakeWeatherDataProvider();
        var service = CreateService(dataProvider);
        var to = Now.AddDays(-3);

        // Act
        await service.GetRangeAsync(new WeatherRangeQuery(To: to), TestContext.Current.CancellationToken);

        // Assert
        var call = dataProvider.RangeCalls.Should().ContainSingle().Which;
        call.From.Should().Be(to - WeatherHistoryService.DefaultRange);
        call.To.Should().Be(to);
    }

    [Fact]
    public async Task GetRangeAsync_RecordsInRange_ReturnsThemNotTruncated()
    {
        // Arrange
        var dataProvider = new FakeWeatherDataProvider();
        dataProvider.Records.Add(CreateRecord(Now.AddHours(-30)));
        dataProvider.Records.Add(CreateRecord(Now.AddHours(-2), 1.5));
        dataProvider.Records.Add(CreateRecord(Now.AddHours(-1), 2.5));
        var service = CreateService(dataProvider);

        // Act
        var result = await service.GetRangeAsync(new WeatherRangeQuery(), TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(WeatherQueryStatus.Ok);
        result.Value!.Truncated.Should().BeFalse();
        result.Value.Records.Select(r => r.OutTemperature).Should().Equal(1.5, 2.5);
    }

    [Fact]
    public async Task GetRangeAsync_MoreRecordsThanLimit_ReturnsNewestRecordsAndTruncated()
    {
        // Arrange
        const int extra = 5;
        var dataProvider = new FakeWeatherDataProvider();
        var start = Now.AddDays(-1);
        for (var i = 0; i < WeatherHistoryService.MaxRecords + extra; i++)
            dataProvider.Records.Add(CreateRecord(start.AddSeconds(i)));
        var service = CreateService(dataProvider);

        // Act
        var result = await service.GetRangeAsync(new WeatherRangeQuery(start, Now),
            TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(WeatherQueryStatus.Ok);
        result.Value!.Truncated.Should().BeTrue();
        var records = result.Value.Records;
        records.Should().HaveCount(WeatherHistoryService.MaxRecords);
        records[0].Time.Should().Be(start.AddSeconds(extra));
        records[^1].Time.Should().Be(start.AddSeconds(WeatherHistoryService.MaxRecords + extra - 1));
        records.Select(r => r.Time).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetRangeAsync_ToNearMinValueWithoutFrom_ReturnsInvalidRange()
    {
        // Arrange
        var dataProvider = new FakeWeatherDataProvider();
        var service = CreateService(dataProvider);

        // Act
        var result = await service.GetRangeAsync(new WeatherRangeQuery(To: DateTimeOffset.MinValue),
            TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(WeatherQueryStatus.InvalidRange);
        dataProvider.RangeCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRangeAsync_RequestCanceledDuringDatabaseFailure_ThrowsOperationCanceledAndDoesNotLog()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();
        // Npgsql can report a canceled read as a broken stream instead of OperationCanceledException.
        var databaseFailure =
            new DataProviderException("Exception while reading from stream", new IOException("Stream closed"));
        var service = CreateService(FakeWeatherDataProvider.Throwing(databaseFailure));

        // Act
        var exception = await Record.ExceptionAsync(() =>
            service.GetRangeAsync(new WeatherRangeQuery(), cancellationTokenSource.Token));

        // Assert
        // ASP.NET Core does not log an OperationCanceledException of an aborted request, any other one as an error.
        var canceled = exception.Should().BeAssignableTo<OperationCanceledException>().Subject;
        canceled.CancellationToken.Should().Be(cancellationTokenSource.Token);
        canceled.InnerException.Should().BeSameAs(databaseFailure);
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRangeAsync_RequestCanceledDuringUnexpectedException_ThrowsOriginalException()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();
        // A bug (e.g. a mapping error) must stay visible even when the client disconnected at the same time.
        var bug = new InvalidOperationException("Error parsing column");
        var service = CreateService(FakeWeatherDataProvider.Throwing(bug));

        // Act
        var exception = await Record.ExceptionAsync(() =>
            service.GetRangeAsync(new WeatherRangeQuery(), cancellationTokenSource.Token));

        // Assert
        exception.Should().BeSameAs(bug);
    }

    [Fact]
    public async Task GetRangeAsync_WithBucket_QueriesAggregatedRange()
    {
        // Arrange
        var dataProvider = new FakeWeatherDataProvider();
        var service = CreateService(dataProvider);

        // Act
        var result = await service.GetRangeAsync(new WeatherRangeQuery(Now.AddDays(-7), Now, BucketMinutes: 30),
            TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(WeatherQueryStatus.Ok);
        var call = dataProvider.RangeCalls.Should().ContainSingle().Which;
        call.Bucket.Should().Be(TimeSpan.FromMinutes(30));
        call.From.Should().Be(Now.AddDays(-7));
        call.Limit.Should().Be(WeatherHistoryService.MaxRecords + 1);
    }

    [Fact]
    public async Task GetRangeAsync_WithoutBucket_QueriesRawRange()
    {
        // Arrange
        var dataProvider = new FakeWeatherDataProvider();
        var service = CreateService(dataProvider);

        // Act
        await service.GetRangeAsync(new WeatherRangeQuery(), TestContext.Current.CancellationToken);

        // Assert
        dataProvider.RangeCalls.Should().ContainSingle().Which.Bucket.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1441)]
    public async Task GetRangeAsync_InvalidBucket_ReturnsInvalidRange(int bucketMinutes)
    {
        // Arrange
        var dataProvider = new FakeWeatherDataProvider();
        var service = CreateService(dataProvider);

        // Act
        var result = await service.GetRangeAsync(new WeatherRangeQuery(BucketMinutes: bucketMinutes),
            TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(WeatherQueryStatus.InvalidRange);
        result.Error.Should().Contain("bucket");
        dataProvider.RangeCalls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1440)]
    public async Task GetRangeAsync_BucketAtLimit_IsAccepted(int bucketMinutes)
    {
        // Arrange
        var service = CreateService(new FakeWeatherDataProvider());

        // Act
        var result = await service.GetRangeAsync(new WeatherRangeQuery(BucketMinutes: bucketMinutes),
            TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(WeatherQueryStatus.Ok);
    }

    [Fact]
    public async Task GetRangeAsync_ToNearMinValueWithNegativeOffset_DoesNotThrowAndQueriesUtcRange()
    {
        // Arrange
        var dataProvider = new FakeWeatherDataProvider();
        var service = CreateService(dataProvider);
        // UTC time is 0001-01-02T02:00, so 24 h before it is valid, but subtracting 24 h from the clock time
        // 0001-01-01T12:00 would throw.
        var to = new DateTimeOffset(new DateTime(1, 1, 1, 12, 0, 0), TimeSpan.FromHours(-14));

        // Act
        var result = await service.GetRangeAsync(new WeatherRangeQuery(To: to), TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(WeatherQueryStatus.Ok);
        var call = dataProvider.RangeCalls.Should().ContainSingle().Which;
        call.To.Should().Be(to);
        call.From.Should().Be(to.ToUniversalTime() - WeatherHistoryService.DefaultRange);
        call.From.Offset.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetRangeAsync_FromNotBeforeTo_ReturnsInvalidRange(int fromOffsetHours)
    {
        // Arrange
        var dataProvider = new FakeWeatherDataProvider();
        var service = CreateService(dataProvider);
        var to = Now.AddHours(-5);

        // Act
        var result = await service.GetRangeAsync(new WeatherRangeQuery(to.AddHours(-fromOffsetHours), to),
            TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(WeatherQueryStatus.InvalidRange);
        result.Error.Should().NotBeNullOrEmpty();
        dataProvider.RangeCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRangeAsync_RangeLongerThanMax_ReturnsInvalidRange()
    {
        // Arrange
        var dataProvider = new FakeWeatherDataProvider();
        var service = CreateService(dataProvider);

        // Act
        var result = await service.GetRangeAsync(
            new WeatherRangeQuery(Now - WeatherHistoryService.MaxRange - TimeSpan.FromMilliseconds(1), Now),
            TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(WeatherQueryStatus.InvalidRange);
        dataProvider.RangeCalls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(6)]
    [InlineData(168)]
    public async Task GetRangeAsync_Hours_QueriesLastHoursBeforeServerNow(int hours)
    {
        // Arrange
        // A client asks for "the last N hours" without its own clock, the server computes 'from'.
        var dataProvider = new FakeWeatherDataProvider();
        var service = CreateService(dataProvider);

        // Act
        var result = await service.GetRangeAsync(new WeatherRangeQuery(Hours: hours),
            TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(WeatherQueryStatus.Ok);
        var call = dataProvider.RangeCalls.Should().ContainSingle().Which;
        call.To.Should().Be(Now);
        call.From.Should().Be(Now.AddHours(-hours));
    }

    [Fact]
    public async Task GetRangeAsync_HoursAndTo_QueriesHoursBeforeTo()
    {
        // Arrange
        var dataProvider = new FakeWeatherDataProvider();
        var service = CreateService(dataProvider);
        var to = Now.AddDays(-2);

        // Act
        await service.GetRangeAsync(new WeatherRangeQuery(To: to, Hours: 3), TestContext.Current.CancellationToken);

        // Assert
        var call = dataProvider.RangeCalls.Should().ContainSingle().Which;
        call.From.Should().Be(to.AddHours(-3));
        call.To.Should().Be(to);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(169)]
    public async Task GetRangeAsync_InvalidHours_ReturnsInvalidRange(int hours)
    {
        // Arrange
        var dataProvider = new FakeWeatherDataProvider();
        var service = CreateService(dataProvider);

        // Act
        var result = await service.GetRangeAsync(new WeatherRangeQuery(Hours: hours),
            TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(WeatherQueryStatus.InvalidRange);
        result.Error.Should().Contain("hours");
        dataProvider.RangeCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRangeAsync_HoursAndFrom_ReturnsInvalidRange()
    {
        // Arrange
        var dataProvider = new FakeWeatherDataProvider();
        var service = CreateService(dataProvider);

        // Act
        var result = await service.GetRangeAsync(new WeatherRangeQuery(From: Now.AddHours(-1), Hours: 6),
            TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(WeatherQueryStatus.InvalidRange);
        dataProvider.RangeCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRangeAsync_RangeExactlyMax_IsAccepted()
    {
        // Arrange
        var service = CreateService(new FakeWeatherDataProvider());

        // Act
        var result = await service.GetRangeAsync(new WeatherRangeQuery(Now - WeatherHistoryService.MaxRange, Now),
            TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(WeatherQueryStatus.Ok);
    }

    [Fact]
    public async Task GetRangeAsync_StorageDisabled_ReturnsStorageDisabled()
    {
        // Arrange
        var dataProvider = new FakeWeatherDataProvider(isEnabled: false);
        var service = CreateService(dataProvider);

        // Act
        var result = await service.GetRangeAsync(new WeatherRangeQuery(), TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(WeatherQueryStatus.StorageDisabled);
        dataProvider.RangeCalls.Should().BeEmpty();
    }

    public static TheoryData<Exception> ExpectedFailures() =>
    [
        new DataProviderException("Failed to connect to postgres:5432", new IOException("Connection refused")),
        new OperationCanceledException(),
    ];

    [Fact]
    public async Task GetLatestAsync_DatabaseFailure_ReturnsUnavailableAndLogsWarningWithCauseWithoutTimeout()
    {
        // Arrange
        var service = CreateService(FakeWeatherDataProvider.Throwing(
            new DataProviderException("Failed to connect to postgres:5432", new IOException("Connection refused"))));

        // Act
        var result = await service.GetLatestAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(WeatherQueryStatus.StorageUnavailable);
        var warning = logger.AtLevel(LogLevel.Warning).Should().ContainSingle().Which;
        warning.Exception.Should().BeNull();
        warning.Message.Should().Contain("Failed to connect to postgres:5432")
            .And.Contain("Cause: Connection refused")
            .And.NotContainEquivalentOf("timed out")
            .And.NotContain("Timeout");
    }

    public static TheoryData<Exception> TimeoutFailures() =>
    [
        new OperationCanceledException(),
        // Npgsql can report the canceled read as a broken stream.
        new DataProviderException("Exception while reading from stream", new IOException("Stream closed")),
    ];

    [Theory]
    [MemberData(nameof(TimeoutFailures))]
    public async Task GetLatestAsync_Timeout_ReturnsUnavailableAndLogsTimeout(Exception failure)
    {
        // Arrange
        // The provider waits until the query timeout cancels its token, then fails like Npgsql would.
        var dataProvider = new FakeWeatherDataProvider(failure)
        {
            BeforeRead = async token =>
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    // The failure is thrown by the fake.
                }
            },
        };
        var service = new WeatherHistoryService(dataProvider, new FakeTimeProvider(Now), logger)
        {
            QueryTimeout = TimeSpan.FromMilliseconds(50),
        };

        // Act
        var result = await service.GetLatestAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(WeatherQueryStatus.StorageUnavailable);
        var warning = logger.AtLevel(LogLevel.Warning).Should().ContainSingle().Which;
        warning.Exception.Should().BeNull();
        warning.Message.Should().Contain("timed out").And.Contain("Timeout: 00:00:00.05")
            .And.NotContain("Cause");
    }

    [Theory]
    [MemberData(nameof(ExpectedFailures))]
    public async Task GetRangeAsync_ExpectedFailure_ReturnsUnavailableAndLogsOneWarning(Exception exception)
    {
        // Arrange
        var service = CreateService(FakeWeatherDataProvider.Throwing(exception));

        // Act
        var result = await service.GetRangeAsync(new WeatherRangeQuery(), TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(WeatherQueryStatus.StorageUnavailable);
        logger.AtLevel(LogLevel.Warning).Should().ContainSingle().Which.Exception.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestAsync_RequestCanceled_ThrowsAndDoesNotLog()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();
        var service = CreateService(FakeWeatherDataProvider.Throwing(
            new OperationCanceledException(cancellationTokenSource.Token)));

        // Act
        var exception = await Record.ExceptionAsync(() => service.GetLatestAsync(cancellationTokenSource.Token));

        // Assert
        exception.Should().BeAssignableTo<OperationCanceledException>();
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestAsync_UnexpectedException_IsNotCaught()
    {
        // Arrange
        var service = CreateService(FakeWeatherDataProvider.Throwing(new InvalidOperationException("boom")));

        // Act
        var exception =
            await Record.ExceptionAsync(() => service.GetLatestAsync(TestContext.Current.CancellationToken));

        // Assert
        exception.Should().BeOfType<InvalidOperationException>();
    }
}
