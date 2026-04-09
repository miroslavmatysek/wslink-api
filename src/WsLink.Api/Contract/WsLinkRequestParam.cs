using Microsoft.AspNetCore.Mvc;

namespace WsLink.Api.Contract;

public class WsLinkRequestParam
{
    /// <summary>
    /// Relative air pressure in hPa.
    /// </summary>
    [FromQuery(Name = "rbar")]
    public float RelativeAirPressure { get; set; }
    
    /// <summary>
    /// Absolute air pressure in hPa.
    /// </summary>
    [FromQuery(Name = "abar")]
    public float AbsoluteAirPressure { get; set; }
    
    /// <summary>
    /// Indoor temperature in celsius degree.
    /// </summary>
    [FromQuery(Name = "intem")]
    public float InTemperature { get; set; }
    
    /// <summary>
    /// Indoor humidity in percentage.
    /// </summary>
    [FromQuery(Name = "inhum")]
    public float InHumanity { get; set; }
    
    /// <summary>
    /// Outdoor temperature in celsius degree.
    /// </summary>
    [FromQuery(Name = "t1tem")]
    public float OutTemperature1 { get; set; }
    
    /// <summary>
    /// Outdoor humidity in percentage.
    /// </summary>
    [FromQuery(Name = "t1hum")]
    public float OutHumanity1 { get; set; }

    /// <summary>
    /// Direction of the wind in degrees.
    /// </summary>
    [FromQuery(Name = "t1wind")]
    public int WindDirection1 { get; set; }
    
    /// <summary>
    /// Wind speed in m/s.
    /// </summary>
    [FromQuery(Name = "t1ws")]
    public double WindSpeed1 { get; set; }
    
    /// <summary>
    /// Wind gust in m/s.
    /// </summary>
    [FromQuery(Name = "t1wgust")]
    public double WindGust1 { get; set; }
    
    /// <summary>
    /// Rainfall in mm/h.
    /// </summary>
    [FromQuery(Name = "t1rainra")]
    public double RainRate { get; set; }
    
    /// <summary>
    /// Hour rainfall in mm.
    /// </summary>
    [FromQuery(Name = "t1rainhr")]
    public double HourlyRainfall { get; set; }
    
    /// <summary>
    /// Day rainfall in mm.
    /// </summary>
    [FromQuery(Name = "t1raindy")]
    public double DailyRainfall { get; set; }
    
    /// <summary>
    /// Week rainfall in mm.
    /// </summary>
    [FromQuery(Name = "t1rainwy")]
    public double WeeklyRainfall { get; set; }

    /// <summary>
    /// Month rainfall in mm.
    /// </summary>
    [FromQuery(Name = "t1rainmth")]
    public double MonthlyRainfall { get; set; }
    
    /// <summary>
    /// Year rainfall in mm.
    /// </summary>
    [FromQuery(Name = "t1rainyr")]
    public double YearlyRainfall { get; set; }
    
    /// <summary>
    /// UVI.
    /// </summary>
    [FromQuery(Name = "t1uvi")]
    public double Uvi { get; set; }
    
    /// <summary>
    /// Light intensity in W/m2.
    /// </summary>
    [FromQuery(Name = "t1solrad")]
    public double LightIntensity { get; set; }
    
}