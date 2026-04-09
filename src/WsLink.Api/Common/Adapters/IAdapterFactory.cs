namespace WsLink.Api.Common.Adapters;

public interface IAdapterFactory
{
    IReadOnlyCollection<IWeatherAdapter> GetWeatherAdapters();
}