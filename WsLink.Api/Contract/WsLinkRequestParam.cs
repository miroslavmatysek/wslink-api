namespace WsLink.Api.Contract;

public class WsLinkRequestParam
{
    public DateTime DateTime { get; set; }
    
    /// <summary>
    /// Relative air pressure in hPa.
    /// </summary>
    public float Rbar { get; set; }
    
    /// <summary>
    /// Absolute air pressure in hPa.
    /// </summary>
    public float Abar { get; set; }
    
    /// <summary>
    /// Indoor temperature in celsius degree.
    /// </summary>
    public float Intem { get; set; }
    
    /// <summary>
    /// Indoor humidity in percentage.
    /// </summary>
    public float Inhum { get; set; }
    
    /// <summary>
    /// Outdoor temperature in celsius degree.
    /// </summary>
    public float T1tem { get; set; }
    
    /// <summary>
    /// Outdoor humidity in percentage.
    /// </summary>
    public float T1hum { get; set; }
}