using System.Collections.Concurrent;
using WsLink.Business.Mqtt;

namespace WsLink.Business.Tests.Fakes;

public sealed record PublishCall(MqttBroker Broker, IReadOnlyList<MqttMessage> Messages, CancellationToken CancellationToken);

/// <summary>
/// Records published messages instead of connecting to a broker, optionally fails with the given exception.
/// </summary>
public sealed class FakeMqttPublisher(Exception? exception = null) : IMqttPublisher
{
    private readonly ConcurrentQueue<PublishCall> calls = new();

    public IReadOnlyList<PublishCall> Calls => calls.ToList();

    public static FakeMqttPublisher Throwing(Exception exception) => new(exception);

    public Task PublishAsync(MqttBroker broker, IReadOnlyCollection<MqttMessage> messages,
        CancellationToken cancellationToken)
    {
        calls.Enqueue(new PublishCall(broker, messages.ToList(), cancellationToken));
        return exception is null ? Task.CompletedTask : Task.FromException(exception);
    }
}
