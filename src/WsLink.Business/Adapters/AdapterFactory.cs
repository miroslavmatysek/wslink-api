using Microsoft.Extensions.DependencyInjection;
using WsLink.Common.Adapters;

namespace WsLink.Business.Adapters;

public class AdapterFactory(
    IEnumerable<IWeatherAdapter> weatherAdapters,
    [FromKeyedServices("dummyWeatherAdapter")] IWeatherAdapter dummyWeatherAdapter)
    : IAdapterFactory
{
    public IReadOnlyCollection<IWeatherAdapter> GetWeatherAdapters()
    {
        var result = weatherAdapters.Where(a => a.IsEnabled).ToList();
        if (!result.Any())
            result.Add(dummyWeatherAdapter);
        return result.AsReadOnly();
    }
}