
using WsLink.Common.Contract;

namespace WsLink.Common.Service;

public interface IWeatherService
{
    Task SaveWeatherData(WsLinkRequestParam requestParam);
}