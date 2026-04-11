using System.Text.Json.Serialization;

namespace WsLink.Api.Contract;

public class WeatherStationData
{
    [JsonPropertyName("out_temp")]
    public double OutTemperature { get; set; }
    
    [JsonPropertyName("out_hum")]
    public double OutHumidity { get; set; }
    
    [JsonPropertyName("in_temp")]
    public double InTemperature { get; set; }
    
    [JsonPropertyName("in_hum")]
    public double InHumanity { get; set; }
    
    [JsonPropertyName("a_pressure")]
    public double AbsolutePressure { get; set; }
    
    [JsonPropertyName("r_pressure")]
    public double RelativePressure { get; set; }
    
    /// <summary>
    /// Direction of the wind in degrees.
    /// </summary>
    [JsonPropertyName("wind_direction")]
    public int WindDirection { get; set; }
    
    /// <summary>
    /// Wind speed in m/s.
    /// </summary>
    [JsonPropertyName("wind_speed")]
    public double WindSpeed { get; set; }
    
    [JsonPropertyName("wind_speed_10min")]
    public double WindSpeed10MinutesAvg { get; set; }
    
    /// <summary>
    /// Wind gust in m/s.
    /// </summary>
    [JsonPropertyName("wind_gust")]
    public double WindGust { get; set; }
    
    /// <summary>
    /// Rainfall in mm/h.
    /// </summary>
    [JsonPropertyName("rain_rate")]
    public double RainRate { get; set; }
    
    /// <summary>
    /// Hour rainfall in mm.
    /// </summary>
    [JsonPropertyName("rainfall_hourly")]
    public double HourlyRainfall { get; set; }
    
    /// <summary>
    /// Day rainfall in mm.
    /// </summary>
    [JsonPropertyName("rainfall_daily")]
    public double DailyRainfall { get; set; }
    
    /// <summary>
    /// Week rainfall in mm.
    /// </summary>
    [JsonPropertyName("rainfall_weekly")]
    public double WeeklyRainfall { get; set; }

    /// <summary>
    /// Month rainfall in mm.
    /// </summary>
    [JsonPropertyName("rainfall_monthly")]
    public double MonthlyRainfall { get; set; }
    
    /// <summary>
    /// Year rainfall in mm.
    /// </summary>
    [JsonPropertyName("rainfall_yearly")]
    public double YearlyRainfall { get; set; }
    
    /// <summary>
    /// UVI.
    /// </summary>
    [JsonPropertyName("uvi")]
    public double Uvi { get; set; }
    
    /// <summary>
    /// Light intensity in W/m2.
    /// </summary>
    [JsonPropertyName("light_intensity")]
    public double LightIntensity { get; set; }
}