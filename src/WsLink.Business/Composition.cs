using Microsoft.Extensions.DependencyInjection;
using WsLink.Business.Adapters;
using WsLink.Business.Service;
using WsLink.Common.Adapters;
using WsLink.Common.Service;

namespace WsLink.Business;

public static class Composition
{
    public static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddSingleton<IWeatherService, WeatherService>();
        services.AddSingleton<IAdapterFactory, AdapterFactory>();

        services.AddSingleton<IWeatherAdapter, HomeAssistantWeatherAdapter>();
        services.AddSingleton<IWeatherAdapter, WindyWeatherAdapter>();
        services.AddKeyedSingleton<IWeatherAdapter, DummyWeatherAdapter>("dummyWeatherAdapter");

        return services;
    }
}