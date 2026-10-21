namespace WsLink.Business.Mqtt;

public sealed record MqttBroker(string Host, int Port, string? Username, string? Password, string ClientId);

public sealed record MqttMessage(string Topic, string Payload);

/// <summary>
/// Publishes messages to an MQTT broker. Hides the MQTT client, so adapters can be tested without a broker.
/// </summary>
public interface IMqttPublisher
{
    Task PublishAsync(MqttBroker broker, IReadOnlyCollection<MqttMessage> messages, CancellationToken cancellationToken);
}
