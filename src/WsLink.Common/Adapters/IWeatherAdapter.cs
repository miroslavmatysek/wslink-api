using WsLink.Common.Contract;

namespace WsLink.Common.Adapters;

public interface IWeatherAdapter
{
    bool IsEnabled { get; }
    
    Task ProcessWeatherDataAsync(WeatherStationData data);
}