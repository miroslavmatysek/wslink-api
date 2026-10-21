using MQTTnet;

namespace WsLink.Business.Mqtt;

public sealed class MqttNetPublisher : IMqttPublisher
{
    private readonly MqttClientFactory clientFactory = new();

    // A connection per upload: the station uploads at most a few times per minute, and there is no reconnect
    // logic or connection state to keep alive between uploads.
    public async Task PublishAsync(MqttBroker broker, IReadOnlyCollection<MqttMessage> messages,
        CancellationToken cancellationToken)
    {
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(broker.Host, broker.Port)
            .WithClientId(broker.ClientId)
            .WithCleanSession();
        if (!string.IsNullOrEmpty(broker.Username))
            builder.WithCredentials(broker.Username, broker.Password);

        // Dispose closes the connection when publishing fails, a clean disconnect is sent only after success.
        using var client = clientFactory.CreateMqttClient();
        await client.ConnectAsync(builder.Build(), cancellationToken).ConfigureAwait(false);
        foreach (var message in messages)
        {
            // Not retained, a retained state would be stored again by Gladys with a new timestamp
            // after every Gladys restart.
            var applicationMessage = new MqttApplicationMessageBuilder()
                .WithTopic(message.Topic)
                .WithPayload(message.Payload)
                .Build();
            await client.PublishAsync(applicationMessage, cancellationToken).ConfigureAwait(false);
        }

        await client.DisconnectAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
