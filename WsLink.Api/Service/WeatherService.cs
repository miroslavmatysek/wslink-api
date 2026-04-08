using WsLink.Api.Common;
using WsLink.Api.Contract;

namespace WsLink.Api.Service;

public class WeatherService : IWeatherService
{
    public Task SaveWeatherData(WsLinkRequestParam requestParam)
    {
        
        return Task.CompletedTask;
    }
}