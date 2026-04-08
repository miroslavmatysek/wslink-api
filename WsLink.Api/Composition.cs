using WsLink.Api.Common;
using WsLink.Api.Service;

namespace WsLink.Api;

public static class Composition
{
    public static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddScoped<IWeatherService, WeatherService>();
        return services;
    }
}