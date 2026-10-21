using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet.Exceptions;
using System.Globalization;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json.Serialization;
using WsLink.Business.Mqtt;
using WsLink.Common.Adapters;
using WsLink.Common.Config;
using WsLink.Common.Contract;

namespace WsLink.Business.Adapters;

/// <summary>
/// Sends the data to Gladys Assistant using its MQTT integration, Gladys has no HTTP endpoint for sensor states.
/// </summary>
public class GladysWeatherAdapter : IWeatherAdapter
{
    // Limit for the whole update (connect + publish), the station request waits until all adapters are processed.
    private static readonly TimeSpan UpdateTimeout = TimeSpan.FromSeconds(10);

    // Gladys MQTT integration accepts only external ids with this prefix.
    private const string ExternalIdPrefix = "mqtt:";

    // Gladys stores numbers only, the binary sensor state is 1/0. A missing value cannot be represented,
    // so it is not sent and Gladys keeps the last state.
    private static readonly Dictionary<string, Func<WeatherStationData, string?>> StateSelectors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(WeatherStationData.OutSensorConnected)] = d => d.OutSensorConnected ? "1" : "0",
            [nameof(WeatherStationData.OutTemperature)] = d => FormatState(d.OutTemperature),
            [nameof(WeatherStationData.OutHumidity)] = d => FormatState(d.OutHumidity),
            [nameof(WeatherStationData.DewPoint)] = d => FormatState(d.DewPoint),
            [nameof(WeatherStationData.InTemperature)] = d => FormatState(d.InTemperature),
            [nameof(WeatherStationData.InHumidity)] = d => FormatState(d.InHumidity),
            [nameof(WeatherStationData.AbsolutePressure)] = d => FormatState(d.AbsolutePressure),
            [nameof(WeatherStationData.RelativePressure)] = d => FormatState(d.RelativePressure),
            [nameof(WeatherStationData.WindDirection)] = d => FormatState(d.WindDirection),
            [nameof(WeatherStationData.WindSpeed)] = d => FormatState(d.WindSpeed),
            [nameof(WeatherStationData.WindSpeed10MinutesAvg)] = d => FormatState(d.WindSpeed10MinutesAvg),
            [nameof(WeatherStationData.WindGust)] = d => FormatState(d.WindGust),
            [nameof(WeatherStationData.RainRate)] = d => FormatState(d.RainRate),
            [nameof(WeatherStationData.HourlyRainfall)] = d => FormatState(d.HourlyRainfall),
            [nameof(WeatherStationData.DailyRainfall)] = d => FormatState(d.DailyRainfall),
            [nameof(WeatherStationData.WeeklyRainfall)] = d => FormatState(d.WeeklyRainfall),
            [nameof(WeatherStationData.MonthlyRainfall)] = d => FormatState(d.MonthlyRainfall),
            [nameof(WeatherStationData.YearlyRainfall)] = d => FormatState(d.YearlyRainfall),
            [nameof(WeatherStationData.Uvi)] = d => FormatState(d.Uvi),
            [nameof(WeatherStationData.LightIntensity)] = d => FormatState(d.LightIntensity),
        };

    private readonly IMqttPublisher publisher;
    private readonly ILogger<GladysWeatherAdapter> logger;
    private readonly MqttBroker? broker;
    private readonly IReadOnlyCollection<GladysFeature> features;

    public GladysWeatherAdapter(IMqttPublisher publisher, IOptions<GladysConfig> options,
        ILogger<GladysWeatherAdapter> logger)
    {
        this.publisher = publisher;
        this.logger = logger;

        broker = CreateBroker(options.Value, logger);
        features = broker is null ? [] : CreateFeatures(options.Value, logger);
    }

    public bool IsEnabled { get => features.Count > 0; }

    public async Task ProcessWeatherDataAsync(WeatherStationData data)
    {
        var messages = features
            .Select(f => (f.Topic, State: f.StateSelector(data)))
            .Where(m => m.State is not null)
            .Select(m => new MqttMessage(m.Topic, m.State!))
            .ToList();
        if (messages.Count == 0)
        {
            logger.LogInformation("No Gladys feature state to send, all mapped values are missing");
            return;
        }

        using var cancellationTokenSource = new CancellationTokenSource(UpdateTimeout);
        try
        {
            await publisher.PublishAsync(broker!, messages, cancellationTokenSource.Token).ConfigureAwait(false);
            logger.LogInformation("Gladys feature states published [Count: {Count}, Skipped: {SkippedCount}]",
                messages.Count, features.Count - messages.Count);
        }
        catch (Exception e) when (e is MqttCommunicationException or SocketException or OperationCanceledException)
        {
            // Expected when the broker is not reachable or rejects the connection (e.g. bad credentials),
            // the stack trace would only flood the log.
            logger.LogWarning("Error publishing Gladys feature states [Error: {Error}, Timeout: {Timeout}]",
                e.Message, UpdateTimeout);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error publishing Gladys feature states");
        }
    }

    private static MqttBroker? CreateBroker(GladysConfig config, ILogger logger)
    {
        if (!config.Enabled)
            return null;

        var host = config.Host?.Trim();
        if (string.IsNullOrEmpty(host) || config.Port is < 1 or > 65535)
        {
            logger.LogError("Invalid Gladys MQTT broker, Gladys adapter is disabled [Host: {Host}, Port: {Port}]",
                config.Host, config.Port);
            return null;
        }

        var clientId = string.IsNullOrWhiteSpace(config.ClientId) ? "wslink-api" : config.ClientId.Trim();
        // Password from env variable or docker secret can contain a trailing new line.
        return new MqttBroker(host, config.Port, config.Username?.Trim(), config.Password?.Trim(), clientId);
    }

    private static IReadOnlyCollection<GladysFeature> CreateFeatures(GladysConfig config, ILogger logger)
    {
        var result = new List<GladysFeature>();

        var deviceExternalId = config.DeviceExternalId?.Trim();
        if (!IsValidExternalId(deviceExternalId))
        {
            logger.LogError(
                "Invalid Gladys DeviceExternalId, Gladys adapter is disabled [DeviceExternalId: {DeviceExternalId}]",
                config.DeviceExternalId);
            return result;
        }

        // The default is applied here and not in appsettings.json, because configuration merges dictionaries
        // per key, so a configured mapping could never replace the default one, only extend it.
        var mapping = config.Features;
        if (mapping.Count == 0)
        {
            mapping = StateSelectors.Keys.ToDictionary(propertyName => propertyName,
                propertyName => $"{deviceExternalId}:{JsonName(propertyName)}");
            logger.LogInformation(
                "No Gladys feature mapping configured, default mapping is used [Prefix: {Prefix}]",
                deviceExternalId + ":");
        }

        foreach (var (propertyName, configuredFeatureId) in mapping)
        {
            var featureId = configuredFeatureId?.Trim();
            if (string.IsNullOrEmpty(featureId))
                continue;

            if (!StateSelectors.TryGetValue(propertyName, out var stateSelector))
                logger.LogWarning("Unknown Gladys feature mapping is ignored [Property: {Property}, Feature: {Feature}]",
                    propertyName, featureId);
            else if (!IsValidExternalId(featureId))
                logger.LogWarning("Invalid Gladys feature external id is ignored [Property: {Property}, Feature: {Feature}]",
                    propertyName, featureId);
            else
                result.Add(new GladysFeature(
                    $"gladys/master/device/{deviceExternalId}/feature/{featureId}/state", stateSelector));
        }

        if (result.Count == 0)
            logger.LogError("No valid Gladys feature mapping, Gladys adapter is disabled");

        return result;
    }

    // An external id is a single topic level, so it must not contain MQTT level separators or wildcards.
    private static bool IsValidExternalId(string? externalId) =>
        externalId is not null &&
        externalId.Length > ExternalIdPrefix.Length &&
        externalId.StartsWith(ExternalIdPrefix, StringComparison.Ordinal) &&
        externalId.IndexOfAny(['/', '+', '#']) < 0 &&
        !externalId.Any(char.IsWhiteSpace);

    private static string JsonName(string propertyName) =>
        typeof(WeatherStationData).GetProperty(propertyName)!.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name;

    private static string? FormatState(double? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string? FormatState(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    private sealed record GladysFeature(string Topic, Func<WeatherStationData, string?> StateSelector);
}
