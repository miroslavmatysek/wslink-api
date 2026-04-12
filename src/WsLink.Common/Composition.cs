using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WsLink.Common.Config;

namespace WsLink.Common;

public static class Composition
{
    public static IServiceCollection AddConfigOptions(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<HomeAssistantConfig>(config.GetSection(HomeAssistantConfig.ConfigSectionName));
        services.Configure<WindyConfig>(config.GetSection(WindyConfig.ConfigSectionName));

        return services;
    }
}