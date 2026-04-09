namespace WsLink.Api.Common.Config;

public class HomeAssistantConfig
{
    public const string ConfigSectionName = "HomeAssistant";
    
    public string? BaseUrl { get; set; }
    
    public string? WebhookId { get; set; }

    public bool Enabled { get; set; } = false;
}