using Microsoft.Extensions.DependencyInjection;
using WsLink.Common.DataProvider;
using WsLink.DataProvider.Database.Postgres;

namespace WsLink.DataProvider;

public static class Composition
{
    public static IServiceCollection AddDataProviders(this IServiceCollection services)
    {
        services.AddSingleton<IWeatherDataProvider, DapperWeatherDataProvider>();

        return services;
    }
}
