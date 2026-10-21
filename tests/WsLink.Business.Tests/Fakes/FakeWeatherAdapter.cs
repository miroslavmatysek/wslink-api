using System.Collections.Concurrent;
using WsLink.Common.Adapters;
using WsLink.Common.Contract;

namespace WsLink.Business.Tests.Fakes;

public sealed class FakeWeatherAdapter(bool isEnabled = true, Func<Task>? process = null) : IWeatherAdapter
{
    private readonly ConcurrentQueue<WeatherStationData> received = new();

    public IReadOnlyList<WeatherStationData> Received => received.ToList();

    public bool IsEnabled { get; } = isEnabled;

    public async Task ProcessWeatherDataAsync(WeatherStationData data)
    {
        received.Enqueue(data);
        if (process is not null)
            await process();
    }
}

public sealed class FakeAdapterFactory(params IWeatherAdapter[] adapters) : IAdapterFactory
{
    public IReadOnlyCollection<IWeatherAdapter> GetWeatherAdapters() => adapters;
}
