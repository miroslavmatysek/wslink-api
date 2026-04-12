namespace WsLink.Common.Config;

public class WindyConfig
{
    public const string ConfigSectionName = "Windy";
    
    public string? StationPassword { get; set; }
    
    public string ApiHost { get; set; } = "https://stations.windy.com";
    
    public bool Enabled { get; set; } = false;
    
    public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromMinutes(5);
    
    public string? StationId { get; set; }
}