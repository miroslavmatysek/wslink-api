using WsLink.Api.Common;
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
            services.AddScoped<IWeatherService, WeatherService>();
            return services;
        }

        public IServiceCollection AddOptions(IConfiguration config)
        {
            services.Configure<HomeAssistantConfig>(config.GetSection(HomeAssistantConfig.ConfigSectionName));
        
            return services;
        }
    }
}