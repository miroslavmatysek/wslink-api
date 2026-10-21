namespace WsLink.Common.Config;

public class GladysConfig
{
    public const string ConfigSectionName = "Gladys";

    /// <summary>
    /// Host of the MQTT broker used by the Gladys MQTT integration.
    /// </summary>
    public string? Host { get; set; }

    public int Port { get; set; } = 1883;

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string ClientId { get; set; } = "wslink-api";

    /// <summary>
    /// External id of the MQTT device created in Gladys, including the "mqtt:" prefix.
    /// </summary>
    public string? DeviceExternalId { get; set; } = "mqtt:wslink";

    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Mapping of <see cref="Contract.WeatherStationData"/> property name to Gladys feature external id.
    /// Only mapped values are sent. When empty, every property is mapped to {DeviceExternalId}:{json_name},
    /// e.g. mqtt:wslink:out_temp.
    /// </summary>
    public Dictionary<string, string> Features { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
