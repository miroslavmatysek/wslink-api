using Microsoft.Extensions.Options;
using WsLink.Api.Common.Adapters;
using WsLink.Api.Common.Config;
using WsLink.Api.Contract;

namespace WsLink.Api.Adapters;

public class HomeAssistantWeatherAdapter : IWeatherAdapter
{
    private const string DefaultHomeAssistantBaseUrl = "http://homeassistant.local:8123";
    private const string HomeAssistantClientName = "HomeAssistant";
    
    private readonly Uri webhookUri;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IOptions<HomeAssistantConfig> options;
    private readonly ILogger<HomeAssistantWeatherAdapter> logger;
    
    public HomeAssistantWeatherAdapter(IHttpClientFactory httpClientFactory, IOptions<HomeAssistantConfig> options, ILogger<HomeAssistantWeatherAdapter> logger)
    {
        this.httpClientFactory = httpClientFactory;
        this.logger = logger;
        this.options = options;
        
        var builder = new UriBuilder(options.Value.BaseUrl ?? DefaultHomeAssistantBaseUrl);
        builder.Path = "api/webhook/" + options.Value.WebhookId;
        webhookUri = builder.Uri;
    }

    public bool IsEnabled { get => options.Value.Enabled; }

    public async Task ProcessWeatherDataAsync(WeatherStationData data)
    {
        try
        {
            var httpClient = httpClientFactory.CreateClient(HomeAssistantClientName);
            using var response = await httpClient
                .PostAsJsonAsync(webhookUri, data)
                .ConfigureAwait(false);

            logger.LogInformation("HomeAssistantWebhookPayload sent [Payload: {@Payload}, status: {StatusCode}]",
                data, response.StatusCode);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error sending HomeAssistantWebhookPayload");
        }
    }
}