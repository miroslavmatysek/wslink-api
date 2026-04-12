using Microsoft.Extensions.Logging;
using WsLink.Common.Adapters;
using WsLink.Common.Contract;

namespace WsLink.Business.Adapters;

public class DummyWeatherAdapter(ILogger<DummyWeatherAdapter> logger) : IWeatherAdapter
{
    public bool IsEnabled { get => true; }

    public Task ProcessWeatherDataAsync(WeatherStationData data)
    {
        logger.LogInformation("DummyWeatherAdapter received data [Data: {@Data}]", data);
        return Task.CompletedTask;
    }
}