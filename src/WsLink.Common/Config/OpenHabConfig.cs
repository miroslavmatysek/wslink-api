namespace WsLink.Common.Config;

public class OpenHabConfig
{
    public const string ConfigSectionName = "OpenHab";

    public string? BaseUrl { get; set; }

    /// <summary>
    /// openHAB API token, sent as Bearer token. Optional when the REST API allows anonymous access.
    /// </summary>
    public string? ApiToken { get; set; }

    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Mapping of <see cref="Contract.WeatherStationData"/> property name to openHAB item name.
    /// Only mapped values are sent.
    /// </summary>
    public Dictionary<string, string> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
