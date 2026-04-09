using WsLink.Api.Contract;

namespace WsLink.Api.Common.Adapters;

public interface IWeatherAdapter
{
    bool IsEnabled { get; }
    
    Task ProcessWeatherDataAsync(WeatherStationData data);
}