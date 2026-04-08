using WsLink.Api.Common;
using WsLink.Api.Contract;

namespace WsLink.Api.Service;

public class WeatherService(IHttpClientFactory httpClientFactory, ILogger<WeatherService> logger) : IWeatherService
{
    public async Task SaveWeatherData(WsLinkRequestParam requestParam)
    {
        var payload = new HomeAssistantWebhookPayload
        {
            OutTemperature = Math.Round(requestParam.T1tem, 1),
            OutHumidity = Math.Round(requestParam.T1hum, 1)
        };

        try
        {
            var httpClient = httpClientFactory.CreateClient("HomeAssistant");
            await httpClient
                .PostAsJsonAsync("http://192.168.88.28:8123/api/webhook/824132b1-7886-4f4c-a70e-a0db96019061", payload)
                .ConfigureAwait(false);
            logger.LogInformation("HomeAssistantWebhookPayload sent [{@Payload}]", payload);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error sending HomeAssistantWebhookPayload");
        }
    }
}