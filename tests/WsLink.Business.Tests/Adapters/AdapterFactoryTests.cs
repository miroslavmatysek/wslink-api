using WsLink.Business.Adapters;
using WsLink.Business.Tests.Fakes;

namespace WsLink.Business.Tests.Adapters;

public class AdapterFactoryTests
{
    private readonly DummyWeatherAdapter dummy = new(new ListLogger<DummyWeatherAdapter>());

    [Fact]
    public void GetWeatherAdapters_ReturnsOnlyEnabledAdapters()
    {
        // Arrange
        var enabled = new FakeWeatherAdapter(isEnabled: true);
        var disabled = new FakeWeatherAdapter(isEnabled: false);
        var factory = new AdapterFactory([enabled, disabled], dummy);

        // Act
        var adapters = factory.GetWeatherAdapters();

        // Assert
        Assert.Same(enabled, Assert.Single(adapters));
    }

    [Fact]
    public void GetWeatherAdapters_NoEnabledAdapter_ReturnsDummyAdapter()
    {
        // Arrange
        var factory = new AdapterFactory([new FakeWeatherAdapter(isEnabled: false)], dummy);

        // Act
        var adapters = factory.GetWeatherAdapters();

        // Assert
        Assert.Same(dummy, Assert.Single(adapters));
    }
}
