using System.Text.Json.Serialization;

namespace WsLink.Api.Contract;

public class HomeAssistantWebhookPayload
{
    [JsonPropertyName("out_temp")]
    public double OutTemperature { get; set; }
    
    [JsonPropertyName("out_hum")]
    public double OutHumidity { get; set; }
}