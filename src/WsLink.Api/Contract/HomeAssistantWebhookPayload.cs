using System.Text.Json.Serialization;

namespace WsLink.Api.Contract;

public class HomeAssistantWebhookPayload
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
}