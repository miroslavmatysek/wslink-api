namespace WsLink.Common.Adapters;

public interface IAdapterFactory
{
    IReadOnlyCollection<IWeatherAdapter> GetWeatherAdapters();
}