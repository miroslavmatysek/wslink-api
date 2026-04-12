using Microsoft.Extensions.Logging;
using WsLink.Common.Adapters;
using WsLink.Common.Contract;
using WsLink.Common.Service;

namespace WsLink.Business.Service;

public class WeatherService(ILogger<WeatherService> logger, IAdapterFactory adapterFactory)
    : IWeatherService
{
    private readonly IReadOnlyCollection<IWeatherAdapter> adapters = adapterFactory.GetWeatherAdapters();

    public async Task SaveWeatherData(WsLinkRequestParam requestParam)
    {
        var payload = new WeatherStationData
        {
            OutTemperature = Math.Round(requestParam.OutTemperature1, 1),
            OutHumidity = Math.Round(requestParam.OutHumanity1, 1),
            InTemperature = Math.Round(requestParam.InTemperature, 1),
            InHumanity = Math.Round(requestParam.InHumanity, 1),
            AbsolutePressure = Math.Round(requestParam.AbsoluteAirPressure, 1),
            RelativePressure = Math.Round(requestParam.RelativeAirPressure, 1),
            WindDirection = requestParam.WindDirection1,
            WindSpeed = Math.Round(requestParam.WindSpeed1, 1),
            WindSpeed10MinutesAvg = Math.Round(requestParam.WindSpeed10MinutesAvg, 1),
            RainRate = Math.Round(requestParam.RainRate, 1),
            HourlyRainfall = Math.Round(requestParam.HourlyRainfall, 1),
            DailyRainfall = Math.Round(requestParam.DailyRainfall, 1),
            WeeklyRainfall = Math.Round(requestParam.WeeklyRainfall, 1),
            MonthlyRainfall = Math.Round(requestParam.MonthlyRainfall, 1),
            YearlyRainfall = Math.Round(requestParam.YearlyRainfall, 1),
            Uvi = Math.Round(requestParam.Uvi, 1),
            LightIntensity = Math.Round(requestParam.LightIntensity, 1),
            WindGust = Math.Round(requestParam.WindGust1, 1),
        };

        logger.LogInformation("Weather data received [Data: {@Payload}]",
            payload);
        
        foreach (var adapter in adapters)
        {
            await adapter.ProcessWeatherDataAsync(payload);
        }
    }
}