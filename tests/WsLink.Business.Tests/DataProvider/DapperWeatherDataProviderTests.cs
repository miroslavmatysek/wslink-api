using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WsLink.Business.Tests.Fakes;
using WsLink.Common.Config;
using WsLink.Common.Contract;
using WsLink.Common.DataProvider;
using WsLink.DataProvider.Database.Postgres;

namespace WsLink.Business.Tests.DataProvider;

/// <summary>
/// A successful insert needs a real server, so these tests check the configuration, the error mapping
/// (against a closed local port) and that the SQL matches the contract.
/// </summary>
public class DapperWeatherDataProviderTests
{
    private const string ConnectionString = "Host=postgres;Database=wslink;Username=wslink;Password=secret";

    // Nothing listens on port 1, so the connection is refused immediately.
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=wslink;Username=wslink;Password=secret;Timeout=2";

    private readonly ListLogger<DapperWeatherDataProvider> logger = new();

    private DapperWeatherDataProvider CreateDataProvider(string? connectionString = ConnectionString,
        bool enabled = true) =>
        new(Options.Create(new PostgresConfig { Enabled = enabled, ConnectionString = connectionString }), logger);

    [Fact]
    public void IsEnabled_CompleteConfig_IsTrue()
    {
        // Arrange
        var dataProvider = CreateDataProvider();

        // Act
        var isEnabled = dataProvider.IsEnabled;

        // Assert
        isEnabled.Should().BeTrue();
        logger.AtLevel(LogLevel.Error).Should().BeEmpty();
    }

    [Fact]
    public void IsEnabled_DisabledInConfig_IsFalse()
    {
        // Arrange
        var dataProvider = CreateDataProvider(enabled: false);

        // Act
        var isEnabled = dataProvider.IsEnabled;

        // Assert
        isEnabled.Should().BeFalse();
        logger.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void IsEnabled_MissingConnectionString_IsFalseAndLogsError(string? connectionString)
    {
        // Arrange
        var dataProvider = CreateDataProvider(connectionString);

        // Act
        var isEnabled = dataProvider.IsEnabled;

        // Assert
        isEnabled.Should().BeFalse();
        logger.AtLevel(LogLevel.Error).Should().ContainSingle();
    }

    [Theory]
    [InlineData("Host=postgres;Database=wslink;Password=secret;NoSuchKey=1")]
    [InlineData("Host=postgres;Database=wslink;Password=secret;Port=abc")]
    [InlineData("Database=wslink;Password=secret")]
    [InlineData("Host=postgres;Password=secret")]
    [InlineData("Host= ;Database=wslink;Password=secret")]
    public void IsEnabled_InvalidConnectionString_IsFalseAndLogsErrorWithoutPassword(string connectionString)
    {
        // Arrange
        var dataProvider = CreateDataProvider(connectionString);

        // Act
        var isEnabled = dataProvider.IsEnabled;

        // Assert
        isEnabled.Should().BeFalse();
        logger.AtLevel(LogLevel.Error).Should().ContainSingle()
            .Which.Message.Should().NotContain("secret");
    }

    [Fact]
    public void IsEnabled_UnquotedPasswordWithSemicolon_LogsErrorWithoutPasswordPart()
    {
        // Arrange
        // The builder splits the password at ';' and fails on the rest as on an unknown keyword,
        // its message contains that part in lower case ("Couldn't set xy7qz").
        var dataProvider = CreateDataProvider("Host=postgres;Database=wslink;Password=Pa55;Xy7Qz=Wv3");

        // Act
        var isEnabled = dataProvider.IsEnabled;

        // Assert
        isEnabled.Should().BeFalse();
        var error = logger.AtLevel(LogLevel.Error).Should().ContainSingle().Which;
        error.Message.Should().NotContainEquivalentOf("Xy7Qz")
            .And.NotContainEquivalentOf("Wv3")
            .And.NotContainEquivalentOf("Pa55");
        error.Exception.Should().BeNull();
    }

    [Fact]
    public void IsEnabled_ConnectionStringWithTrailingNewLine_IsTrue()
    {
        // Arrange
        var dataProvider = CreateDataProvider(ConnectionString + "\n");

        // Act
        var isEnabled = dataProvider.IsEnabled;

        // Assert
        isEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task InsertAsync_ServerUnreachable_ThrowsDataProviderException()
    {
        // Arrange
        var dataProvider = CreateDataProvider(UnreachableConnectionString);

        // Act
        var exception = await Record.ExceptionAsync(() => dataProvider.InsertAsync(DateTimeOffset.UtcNow,
            new WeatherStationData(), TestContext.Current.CancellationToken));

        // Assert
        var dataProviderException = exception.Should().BeOfType<DataProviderException>().Subject;
        dataProviderException.InnerException.Should().NotBeNull();
        dataProviderException.Message.Should().Be(dataProviderException.InnerException!.Message)
            .And.NotContain("secret");
    }

    [Fact]
    public async Task InsertAsync_Canceled_ThrowsOperationCanceledException()
    {
        // Arrange
        var dataProvider = CreateDataProvider(UnreachableConnectionString);
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        // Act
        var exception = await Record.ExceptionAsync(() => dataProvider.InsertAsync(DateTimeOffset.UtcNow,
            new WeatherStationData(), cancellationTokenSource.Token));

        // Assert
        exception.Should().BeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task InsertAsync_Disabled_ThrowsInvalidOperationException()
    {
        // Arrange
        var dataProvider = CreateDataProvider(enabled: false);

        // Act
        var exception = await Record.ExceptionAsync(() => dataProvider.InsertAsync(DateTimeOffset.UtcNow,
            new WeatherStationData(), TestContext.Current.CancellationToken));

        // Assert
        exception.Should().BeOfType<InvalidOperationException>();
    }

    public static TheoryData<string, string> Properties()
    {
        var data = new TheoryData<string, string>();
        foreach (var property in typeof(WeatherStationData).GetProperties())
            data.Add(property.Name, property.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Properties))]
    public void InsertSql_EveryProperty_IsInsertedIntoColumnWithJsonName(string propertyName, string columnName)
    {
        // Arrange
        var sql = DapperWeatherDataProvider.InsertSql;

        // Act
        var columns = ColumnList(sql, "weather_data");
        var values = ColumnList(sql, "VALUES");

        // Assert
        columns.Should().Contain(columnName);
        values[columns.IndexOf(columnName)].Should().Be("@" + propertyName);
    }

    [Fact]
    public void InsertSql_Time_IsFirstColumn()
    {
        // Arrange
        var sql = DapperWeatherDataProvider.InsertSql;

        // Act
        var columns = ColumnList(sql, "weather_data");
        var values = ColumnList(sql, "VALUES");

        // Assert
        columns.Should().HaveCount(typeof(WeatherStationData).GetProperties().Length + 1);
        values.Should().HaveSameCount(columns);
        columns[0].Should().Be("time");
        values[0].Should().Be("@Time");
    }

    public static TheoryData<string, string> RecordProperties()
    {
        var data = new TheoryData<string, string>();
        foreach (var property in typeof(WeatherRecord).GetProperties())
            data.Add(property.Name, property.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(RecordProperties))]
    public void SelectColumns_EveryRecordProperty_IsReadFromColumnWithJsonName(string propertyName, string columnName)
    {
        // Arrange
        var columns = DapperWeatherDataProvider.SelectColumns;

        // Act
        var selected = columns.Split(',', StringSplitOptions.TrimEntries);

        // Assert
        selected.Should().Contain($"{columnName} AS {propertyName}");
    }

    [Fact]
    public void SelectColumns_HasOneColumnPerRecordProperty()
    {
        // Arrange
        var columns = DapperWeatherDataProvider.SelectColumns;

        // Act
        var selected = columns.Split(',', StringSplitOptions.TrimEntries);

        // Assert
        selected.Should().HaveCount(typeof(WeatherRecord).GetProperties().Length).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void SelectRangeSql_FiltersHalfOpenRangeAndLimitsNewestRows()
    {
        // Arrange
        var sql = DapperWeatherDataProvider.SelectRangeSql;

        // Act
        var normalized = Regex.Replace(sql, @"\s+", " ");

        // Assert
        // Newest first, so LIMIT drops the oldest rows; the result is reversed by NewestFirstToAscending.
        normalized.Should().Contain("WHERE time >= @From AND time < @To ORDER BY time DESC LIMIT @Limit");
    }

    [Fact]
    public void SelectLatestSql_ReturnsNewestRow()
    {
        // Arrange
        var sql = DapperWeatherDataProvider.SelectLatestSql;

        // Act
        var normalized = Regex.Replace(sql, @"\s+", " ");

        // Assert
        normalized.Should().EndWith("ORDER BY time DESC LIMIT 1");
    }

    [Fact]
    public void AggregatedColumns_EveryRecordPropertyHasExactlyOneAlias()
    {
        // Arrange
        var columns = DapperWeatherDataProvider.AggregatedColumns;

        // Act
        var aliases = Regex.Matches(columns, @"\bAS (\w+)").Select(m => m.Groups[1].Value).ToList();

        // Assert
        aliases.Should().BeEquivalentTo(typeof(WeatherRecord).GetProperties().Select(p => p.Name));
    }

    [Fact]
    public void SelectAggregatedRangeSql_GroupsByBucketAndLimitsNewestBuckets()
    {
        // Arrange
        var sql = DapperWeatherDataProvider.SelectAggregatedRangeSql;

        // Act
        var normalized = Regex.Replace(sql, @"\s+", " ");

        // Assert
        normalized.Should().Contain("time_bucket(@Bucket, time) AS Time")
            .And.Contain("WHERE time >= @From AND time < @To GROUP BY 1 ORDER BY 1 DESC LIMIT @Limit");
    }

    [Fact]
    public void NewestFirstToAscending_NewestFirstRows_ReturnsAscendingTime()
    {
        // Arrange
        var start = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var newestFirst = new List<WeatherRecord>
        {
            new() { Time = start.AddMinutes(2) },
            new() { Time = start.AddMinutes(1) },
            new() { Time = start },
        };

        // Act
        var result = DapperWeatherDataProvider.NewestFirstToAscending(newestFirst);

        // Assert
        result.Select(r => r.Time).Should().Equal(start, start.AddMinutes(1), start.AddMinutes(2));
    }

    [Fact]
    public void CreateTableScript_ContainsEveryInsertedColumn()
    {
        // Arrange
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "database", "timescaledb.sql"));
        var columns = ColumnList(DapperWeatherDataProvider.InsertSql, "weather_data");

        // Act
        var missing = columns
            .Where(column => !Regex.IsMatch(script, $@"^\s+{column}\s", RegexOptions.Multiline))
            .ToList();

        // Assert
        missing.Should().BeEmpty();
    }

    private static List<string> ColumnList(string sql, string after)
    {
        var start = sql.IndexOf('(', sql.IndexOf(after, StringComparison.Ordinal));
        var end = sql.IndexOf(')', start);
        return sql[(start + 1)..end].Split(',', StringSplitOptions.TrimEntries).ToList();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WsLink.Api.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }
}
