using System.Globalization;

namespace WsLink.Business.Tests.Fakes;

/// <summary>
/// Sets the current culture until disposed, e.g. to check that a decimal comma culture does not leak into the output.
/// </summary>
public sealed class CultureScope : IDisposable
{
    private readonly CultureInfo originalCulture = CultureInfo.CurrentCulture;

    public CultureScope(string cultureName) => CultureInfo.CurrentCulture = new CultureInfo(cultureName);

    public void Dispose() => CultureInfo.CurrentCulture = originalCulture;
}
