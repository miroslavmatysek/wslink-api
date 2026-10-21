using WsLink.Business.Mqtt;

namespace WsLink.Business.Tests.Mqtt;

public class MqttNetPublisherTests
{
    [Fact]
    public void CreateClientId_AddsUniqueSuffixWithinMqttLimit()
    {
        // Arrange
        const string clientId = "wslink-api";

        // Act
        var first = MqttNetPublisher.CreateClientId(clientId);
        var second = MqttNetPublisher.CreateClientId(clientId);

        // Assert
        Assert.StartsWith("wslink-api-", first);
        Assert.NotEqual(first, second);
        Assert.True(first.Length <= 23);
    }
}
