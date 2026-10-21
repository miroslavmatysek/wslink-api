using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using WsLink.Common.Adapters;
using WsLink.Common.Config;
using WsLink.Common.Contract;

namespace WsLink.Business.Adapters;

public class OpenHabWeatherAdapter : IWeatherAdapter
{
    internal const string OpenHabClientName = "OpenHab";

    // Every item is a separate request, limit them so a small openHAB host is not flooded.
    private const int MaxParallelRequests = 4;

    // Limit for the whole update, the station request waits until all adapters are processed.
    // It is the only timeout, the openHAB HttpClient has none, so the timeout is always reported the same way.
    private static readonly TimeSpan UpdateTimeout = TimeSpan.FromSeconds(10);

    // openHAB state for an unknown value, used when the station did not send the value.
    private const string UndefinedState = "UNDEF";

    // Item name prefix of the default mapping, used when no item is configured.
    private const string DefaultItemPrefix = "WsLink_";

    // Values are sent with the station units, so openHAB converts them for dimensioned items (e.g. Number:Speed
    // defaults to km/h). Plain Number items ignore the unit. Humidity is sent without "%", because the default unit
    // of Number:Dimensionless is "one" and 45 % would be stored as 0.45.
    private static readonly Dictionary<string, Func<WeatherStationData, string>> StateSelectors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(WeatherStationData.OutSensorConnected)] = d => d.OutSensorConnected ? "ON" : "OFF",
            [nameof(WeatherStationData.OutTemperature)] = d => FormatState(d.OutTemperature, "°C"),
            [nameof(WeatherStationData.OutHumidity)] = d => FormatState(d.OutHumidity),
            [nameof(WeatherStationData.DewPoint)] = d => FormatState(d.DewPoint, "°C"),
            [nameof(WeatherStationData.InTemperature)] = d => FormatState(d.InTemperature, "°C"),
            [nameof(WeatherStationData.InHumidity)] = d => FormatState(d.InHumidity),
            [nameof(WeatherStationData.AbsolutePressure)] = d => FormatState(d.AbsolutePressure, "hPa"),
            [nameof(WeatherStationData.RelativePressure)] = d => FormatState(d.RelativePressure, "hPa"),
            [nameof(WeatherStationData.WindDirection)] = d => FormatState(d.WindDirection, "°"),
            [nameof(WeatherStationData.WindSpeed)] = d => FormatState(d.WindSpeed, "m/s"),
            [nameof(WeatherStationData.WindSpeed10MinutesAvg)] = d => FormatState(d.WindSpeed10MinutesAvg, "m/s"),
            [nameof(WeatherStationData.WindGust)] = d => FormatState(d.WindGust, "m/s"),
            [nameof(WeatherStationData.RainRate)] = d => FormatState(d.RainRate, "mm/h"),
            [nameof(WeatherStationData.HourlyRainfall)] = d => FormatState(d.HourlyRainfall, "mm"),
            [nameof(WeatherStationData.DailyRainfall)] = d => FormatState(d.DailyRainfall, "mm"),
            [nameof(WeatherStationData.WeeklyRainfall)] = d => FormatState(d.WeeklyRainfall, "mm"),
            [nameof(WeatherStationData.MonthlyRainfall)] = d => FormatState(d.MonthlyRainfall, "mm"),
            [nameof(WeatherStationData.YearlyRainfall)] = d => FormatState(d.YearlyRainfall, "mm"),
            [nameof(WeatherStationData.Uvi)] = d => FormatState(d.Uvi),
            [nameof(WeatherStationData.LightIntensity)] = d => FormatState(d.LightIntensity, "W/m²"),
        };

    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<OpenHabWeatherAdapter> logger;
    private readonly AuthenticationHeaderValue? authorization;
    private readonly IReadOnlyCollection<OpenHabItem> items;

    public OpenHabWeatherAdapter(IHttpClientFactory httpClientFactory, IOptions<OpenHabConfig> options,
        ILogger<OpenHabWeatherAdapter> logger)
    {
        this.httpClientFactory = httpClientFactory;
        this.logger = logger;

        // Token from env variable or docker secret can contain a trailing new line, which is invalid in a header.
        var apiToken = options.Value.ApiToken?.Trim();
        authorization = string.IsNullOrEmpty(apiToken) ? null : new AuthenticationHeaderValue("Bearer", apiToken);
        items = CreateItems(options.Value, logger);
    }

    public bool IsEnabled { get => items.Count > 0; }

    public async Task ProcessWeatherDataAsync(WeatherStationData data)
    {
        var httpClient = httpClientFactory.CreateClient(OpenHabClientName);
        using var cancellationTokenSource = new CancellationTokenSource(UpdateTimeout);
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxParallelRequests, CancellationToken = cancellationTokenSource.Token
        };

        var updatedCount = 0;
        string? firstError = null;
        var timedOut = false;
        try
        {
            await Parallel.ForEachAsync(items, parallelOptions, async (item, cancellationToken) =>
                {
                    var error = await UpdateItemStateAsync(httpClient, item, item.StateSelector(data), cancellationToken);
                    if (error is null)
                        Interlocked.Increment(ref updatedCount);
                    else
                        Interlocked.CompareExchange(ref firstError, error, null);
                })
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
        }

        if (updatedCount == items.Count)
            logger.LogInformation("openHAB items updated [Count: {Count}]", updatedCount);
        else
            // Item failures are logged only on Debug level, so an unreachable openHAB produces one line per update.
            logger.LogWarning(
                "openHAB items update failed [Failed: {FailedCount}, Total: {TotalCount}, FirstError: {FirstError}, TimedOut: {TimedOut}, Timeout: {Timeout}]",
                items.Count - updatedCount, items.Count, firstError, timedOut, UpdateTimeout);
    }

    // Returns null when the state is updated, otherwise the error description.
    private async Task<string?> UpdateItemStateAsync(HttpClient httpClient, OpenHabItem item, string state,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, item.StateUri);
            request.Content = new StringContent(state, Encoding.UTF8, "text/plain");
            request.Headers.Authorization = authorization;

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                logger.LogDebug("openHAB item state updated [Item: {Item}, State: {State}]", item.Name, state);
                return null;
            }

            logger.LogDebug("openHAB item state update failed [Item: {Item}, State: {State}, Status: {StatusCode}]",
                item.Name, state, response.StatusCode);
            return $"{item.Name}: status {(int)response.StatusCode} {response.StatusCode}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Timeout of the whole update, it is logged once by the caller.
            return $"{item.Name}: timeout";
        }
        catch (HttpRequestException e)
        {
            // Expected when openHAB is not reachable, the stack trace would only flood the log.
            logger.LogDebug("openHAB item state update failed [Item: {Item}, Error: {Error}]", item.Name, e.Message);
            return $"{item.Name}: {e.Message}";
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error updating openHAB item state [Item: {Item}]", item.Name);
            return $"{item.Name}: {e.Message}";
        }
    }

    private static IReadOnlyCollection<OpenHabItem> CreateItems(OpenHabConfig config, ILogger logger)
    {
        var result = new List<OpenHabItem>();
        if (!config.Enabled)
            return result;

        // Trailing slash keeps a sub-path of BaseUrl (e.g. openHAB behind a reverse proxy) in the item URIs.
        if (!Uri.TryCreate(config.BaseUrl?.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            logger.LogError("Invalid openHAB BaseUrl, openHAB adapter is disabled [BaseUrl: {BaseUrl}]", config.BaseUrl);
            return result;
        }

        // The default is applied here and not in appsettings.json, because configuration merges dictionaries
        // per key, so a configured mapping could never replace the default one, only extend it.
        var mapping = config.Items;
        if (mapping.Count == 0)
        {
            mapping = StateSelectors.Keys.ToDictionary(propertyName => propertyName,
                propertyName => DefaultItemPrefix + propertyName);
            logger.LogInformation("No openHAB item mapping configured, default mapping is used [Prefix: {Prefix}]",
                DefaultItemPrefix);
        }

        foreach (var (propertyName, configuredItemName) in mapping)
        {
            // openHAB item names cannot contain whitespace, a stray space from env variables would only cause 404.
            var itemName = configuredItemName?.Trim();
            if (string.IsNullOrEmpty(itemName))
                continue;

            if (StateSelectors.TryGetValue(propertyName, out var stateSelector))
                result.Add(new OpenHabItem(itemName,
                    new Uri(baseUri, $"rest/items/{Uri.EscapeDataString(itemName)}/state"), stateSelector));
            else
                logger.LogWarning("Unknown openHAB item mapping is ignored [Property: {Property}, Item: {Item}]",
                    propertyName, itemName);
        }

        if (result.Count == 0)
            logger.LogError("No valid openHAB item mapping, openHAB adapter is disabled");

        return result;
    }

    private static string FormatState(double? value, string? unit = null) =>
        value.HasValue ? WithUnit(value.Value.ToString(CultureInfo.InvariantCulture), unit) : UndefinedState;

    private static string WithUnit(string value, string? unit) => unit is null ? value : $"{value} {unit}";

    private sealed record OpenHabItem(string Name, Uri StateUri, Func<WeatherStationData, string> StateSelector);
}
