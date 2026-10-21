using Microsoft.Extensions.Logging;
using WsLink.Common.Adapters;
using WsLink.Common.Contract;
using WsLink.Common.Service;

namespace WsLink.Business.Service;

public class WeatherService(ILogger<WeatherService> logger, IAdapterFactory adapterFactory)
    : IWeatherService
{
    private readonly IReadOnlyCollection<IWeatherAdapter> adapters = adapterFactory.GetWeatherAdapters();

    private bool? lastOutSensorConnected;

    public async Task SaveWeatherData(WsLinkRequestParam requestParam)
    {
        // When the outdoor sensor is disconnected, the station does not send valid outdoor values.
        var outSensorConnected = requestParam.OutSensorConnection != 0;
        LogOutSensorConnectionChange(outSensorConnected);

        double? Outdoor(double? value) => outSensorConnected ? Round(value) : null;

        var payload = new WeatherStationData
        {
            OutSensorConnected = outSensorConnected,
            OutTemperature = Outdoor(requestParam.OutTemperature1),
            OutHumidity = Outdoor(requestParam.OutHumidity1),
            InTemperature = Round(requestParam.InTemperature),
            InHumidity = Round(requestParam.InHumidity),
            AbsolutePressure = Round(requestParam.AbsoluteAirPressure),
            RelativePressure = Round(requestParam.RelativeAirPressure),
            WindDirection = outSensorConnected ? requestParam.WindDirection1 : null,
            WindSpeed = Outdoor(requestParam.WindSpeed1),
            WindSpeed10MinutesAvg = Outdoor(requestParam.WindSpeed10MinutesAvg),
            RainRate = Outdoor(requestParam.RainRate),
            HourlyRainfall = Outdoor(requestParam.HourlyRainfall),
            DailyRainfall = Outdoor(requestParam.DailyRainfall),
            WeeklyRainfall = Outdoor(requestParam.WeeklyRainfall),
            MonthlyRainfall = Outdoor(requestParam.MonthlyRainfall),
            YearlyRainfall = Outdoor(requestParam.YearlyRainfall),
            Uvi = Outdoor(requestParam.Uvi),
            LightIntensity = Outdoor(requestParam.LightIntensity),
            WindGust = Outdoor(requestParam.WindGust1),
        };

        logger.LogInformation("Weather data received [Data: {@Payload}]",
            payload);

        // Adapters are independent, so a slow target does not delay the others.
        await Task.WhenAll(adapters.Select(adapter => adapter.ProcessWeatherDataAsync(payload)));
    }

    private void LogOutSensorConnectionChange(bool outSensorConnected)
    {
        if (lastOutSensorConnected == outSensorConnected)
            return;

        if (outSensorConnected)
            logger.LogInformation("Outdoor sensor is connected");
        else
            logger.LogWarning("Outdoor sensor is not connected, outdoor values are ignored");

        lastOutSensorConnected = outSensorConnected;
    }

    // Non-finite values (e.g. "NaN" in the query) are treated as missing, they cannot be serialized to JSON.
    // Adding 0.0 turns negative zero (e.g. rounded -0.04) into 0, so "-0" is never sent.
    private static double? Round(double? value) =>
        value.HasValue && double.IsFinite(value.Value) ? Math.Round(value.Value, 1) + 0.0 : null;
}
