using WsLink.Api.Contract;

namespace WsLink.Api.Common;

public interface IWeatherService
{
    Task SaveWeatherData(WsLinkRequestParam requestParam);
}