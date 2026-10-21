using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WsLink.Common.Adapters;
using WsLink.Common.Config;
using WsLink.Common.Contract;

namespace WsLink.Business.Adapters;

public class HomeAssistantWeatherAdapter : IWeatherAdapter
{
    private const string DefaultHomeAssistantBaseUrl = "http://homeassistant.local:8123";
    internal const string HomeAssistantClientName = "HomeAssistant";

    // Missing values are not sent at all, so Home Assistant does not receive them as zeros or nulls.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    
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
                .PostAsJsonAsync(webhookUri, data, SerializerOptions)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
                logger.LogInformation("HomeAssistantWebhookPayload sent [Payload: {@Payload}, status: {StatusCode}]",
                    data, response.StatusCode);
            else
                logger.LogWarning("HomeAssistantWebhookPayload rejected [Payload: {@Payload}, status: {StatusCode}]",
                    data, response.StatusCode);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // Expected when Home Assistant is not reachable, the stack trace would only flood the log.
            logger.LogWarning("Error sending HomeAssistantWebhookPayload [Error: {Error}]", e.Message);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error sending HomeAssistantWebhookPayload");
        }
    }
}