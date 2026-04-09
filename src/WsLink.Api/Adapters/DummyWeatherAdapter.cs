using WsLink.Api.Common.Adapters;
using WsLink.Api.Contract;

namespace WsLink.Api.Adapters;

public class DummyWeatherAdapter(ILogger<DummyWeatherAdapter> logger) : IWeatherAdapter
{
    public bool IsEnabled { get => true; }

    public Task ProcessWeatherDataAsync(WeatherStationData data)
    {
        logger.LogInformation("DummyWeatherAdapter received data [Data: {@Data}]", data);
        return Task.CompletedTask;
    }
}