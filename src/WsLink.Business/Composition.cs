using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WsLink.Business.Adapters;
using WsLink.Business.Mqtt;
using WsLink.Business.Service;
using WsLink.Common.Adapters;
using WsLink.Common.Service;

namespace WsLink.Business;

public static class Composition
{
    private static readonly TimeSpan AdapterTimeout = TimeSpan.FromSeconds(10);

    public static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);
        // The station request waits until all adapters are processed, so an unreachable target must not block it.
        services.AddHttpClient(HomeAssistantWeatherAdapter.HomeAssistantClientName,
            client => client.Timeout = AdapterTimeout);
        services.AddHttpClient(WindyWeatherAdapter.HttpClientName, client => client.Timeout = AdapterTimeout);
        // openHAB adapter limits the whole update by its own timeout.
        services.AddHttpClient(OpenHabWeatherAdapter.OpenHabClientName,
            client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton<IMqttPublisher, MqttNetPublisher>();
        services.AddSingleton<IWeatherService, WeatherService>();
        services.AddSingleton<IAdapterFactory, AdapterFactory>();

        services.AddSingleton<IWeatherAdapter, HomeAssistantWeatherAdapter>();
        services.AddSingleton<IWeatherAdapter, WindyWeatherAdapter>();
        services.AddSingleton<IWeatherAdapter, OpenHabWeatherAdapter>();
        services.AddSingleton<IWeatherAdapter, GladysWeatherAdapter>();
        services.AddKeyedSingleton<IWeatherAdapter, DummyWeatherAdapter>("dummyWeatherAdapter");

        return services;
    }
}