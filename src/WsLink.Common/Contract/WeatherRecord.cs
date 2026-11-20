using System.Text.Json.Serialization;

namespace WsLink.Common.Contract;

/// <summary>
/// A stored measurement: the values as sent to the targets and the time the service received them.
/// </summary>
public class WeatherRecord : WeatherStationData
{
    /// <summary>
    /// Time of receipt by the service (UTC).
    /// </summary>
    [JsonPropertyName("time")]
    [JsonPropertyOrder(-1)]
    public DateTimeOffset Time { get; set; }
}
