using WsLink.Api.Common.Adapters;

namespace WsLink.Api.Adapters;

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