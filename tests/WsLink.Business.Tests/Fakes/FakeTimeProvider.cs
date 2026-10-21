namespace WsLink.Business.Tests.Fakes;

/// <summary>
/// Time provider with a manually advanced clock, so interval tests do not depend on the real time.
/// </summary>
public sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => utcNow;

    public void Advance(TimeSpan delta) => utcNow += delta;
}
