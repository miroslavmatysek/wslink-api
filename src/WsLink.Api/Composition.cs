using WsLink.Api.Adapters;
using WsLink.Api.Common;
using WsLink.Api.Common.Adapters;
using WsLink.Api.Common.Config;
using WsLink.Api.Service;

namespace WsLink.Api;

public static class Composition
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddServices()
        {
            services.AddHttpClient();
            services.AddSingleton<IWeatherService, WeatherService>();
            services.AddSingleton<IAdapterFactory, AdapterFactory>();

            services.AddSingleton<IWeatherAdapter, HomeAssistantWeatherAdapter>();
            services.AddKeyedSingleton<IWeatherAdapter, DummyWeatherAdapter>("dummyWeatherAdapter");

            return services;
        }

        public IServiceCollection AddOptions(IConfiguration config)
        {
            services.Configure<HomeAssistantConfig>(config.GetSection(HomeAssistantConfig.ConfigSectionName));

            return services;
        }
    }
}