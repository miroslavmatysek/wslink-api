using System.Collections.Concurrent;
using WsLink.Common.Contract;
using WsLink.Common.DataProvider;

namespace WsLink.Business.Tests.Fakes;

public sealed record InsertCall(DateTimeOffset Time, WeatherStationData Data, CancellationToken CancellationToken);

/// <summary>
/// Records inserts instead of connecting to a database, optionally fails with the given exception.
/// </summary>
public sealed class FakeWeatherDataProvider(Exception? exception = null, bool isEnabled = true) : IWeatherDataProvider
{
    private readonly ConcurrentQueue<InsertCall> calls = new();

    public IReadOnlyList<InsertCall> Calls => calls.ToList();

    public bool IsEnabled { get; } = isEnabled;

    public static FakeWeatherDataProvider Throwing(Exception exception) => new(exception);

    public Task InsertAsync(DateTimeOffset time, WeatherStationData data, CancellationToken cancellationToken)
    {
        calls.Enqueue(new InsertCall(time, data, cancellationToken));
        return exception is null ? Task.CompletedTask : Task.FromException(exception);
    }
}
