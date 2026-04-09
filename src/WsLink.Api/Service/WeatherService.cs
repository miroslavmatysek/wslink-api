using Microsoft.Extensions.Options;
using WsLink.Api.Common;
using WsLink.Api.Common.Config;
using WsLink.Api.Contract;

namespace WsLink.Api.Service;

public class WeatherService : IWeatherService
{
    private const string DefaultHomeAssistantBaseUrl = "http://homeassistant.local:8123";

    private readonly IHttpClientFactory httpClientFactory;
    private readonly IOptions<HomeAssistantConfig> options;
    private readonly ILogger<WeatherService> logger;
    private readonly Uri webhookUri;

    public WeatherService(IHttpClientFactory httpClientFactory, IOptions<HomeAssistantConfig> options,
        ILogger<WeatherService> logger)
    {
        this.httpClientFactory = httpClientFactory;
        this.options = options;
        this.logger = logger;

        var builder = new UriBuilder(options.Value.BaseUrl ?? DefaultHomeAssistantBaseUrl);
        builder.Path = "api/webhook/" + options.Value.WebhookId;
        webhookUri = builder.Uri;
    }

    private const string HomeAssistantClientName = "HomeAssistant";

    public async Task SaveWeatherData(WsLinkRequestParam requestParam)
    {
        if (!options.Value.Enabled)
            return;

        var payload = new HomeAssistantWebhookPayload
        {
            OutTemperature = Math.Round(requestParam.T1tem, 1),
            OutHumidity = Math.Round(requestParam.T1hum, 1),
            InTemperature = Math.Round(requestParam.Intem, 1),
            InHumanity = Math.Round(requestParam.Inhum, 1),
            AbsolutePressure = Math.Round(requestParam.Abar, 1),
            RelativePressure = Math.Round(requestParam.Rbar, 1),
        };

        try
        {
            var httpClient = httpClientFactory.CreateClient(HomeAssistantClientName);
            using var response = await httpClient
                .PostAsJsonAsync(webhookUri, payload)
                .ConfigureAwait(false);

            logger.LogInformation("HomeAssistantWebhookPayload sent [Payload: {@Payload}, status: {StatusCode}]",
                payload, response.StatusCode);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error sending HomeAssistantWebhookPayload");
        }
    }
}