---
name: add-weather-adapter
description: Add a new weather data target (e.g. MQTT, InfluxDB, Weather Underground) to WsLink.Api as an IWeatherAdapter, including config, DI registration, tests and docs. Use when the user wants to send station data to a new service.
---

# Add a weather adapter

Follow `docs/specs/weather-forwarding.md` section 2 (adapter contract). Use the existing adapters as templates:
`HomeAssistantWeatherAdapter` (single JSON POST), `WindyWeatherAdapter` (rate limited GET),
`OpenHabWeatherAdapter` (configurable mapping, many requests, own overall timeout).

## Checklist

1. **Config** – `src/WsLink.Common/Config/<Name>Config.cs` with `ConfigSectionName`, `Enabled` (default `false`)
   and the target settings. Register it in `src/WsLink.Common/Composition.cs`
   (`services.Configure<...>(config.GetSection(...))`).
2. **Adapter** – `src/WsLink.Business/Adapters/<Name>WeatherAdapter.cs` implementing `IWeatherAdapter`:
   - `internal const string <Name>ClientName` for the named HttpClient,
   - `IsEnabled` false for incomplete config (validate once, log an Error for invalid values),
   - catch all exceptions inside `ProcessWeatherDataAsync`; log levels per the spec table
     (expected HTTP failures as Warning with `e.Message` and no exception object),
   - check `response.IsSuccessStatusCode` and log rejected requests as Warning,
   - format numbers with `CultureInfo.InvariantCulture`, skip or mark missing (`null`) values,
   - finish within 10 s.
3. **DI** – in `src/WsLink.Business/Composition.cs` add `services.AddHttpClient(<ClientName>, client => client.Timeout = AdapterTimeout)`
   (or `Timeout.InfiniteTimeSpan` with a comment when the adapter has its own overall timeout)
   and `services.AddSingleton<IWeatherAdapter, <Name>WeatherAdapter>()`.
4. **appsettings.json** – add the section with `"Enabled": false` and non-secret defaults only.
5. **Tests** – `tests/WsLink.Business.Tests/Adapters/<Name>WeatherAdapterTests.cs`, AAA style, using
   `FakeHttpMessageHandler`, `FakeHttpClientFactory` and `ListLogger<T>`. Cover at least:
   `IsEnabled` for complete and each kind of incomplete config, request method/URL/headers/body,
   missing values, invariant culture (`CultureScope("cs-CZ")`), success log, error status → one Warning,
   unreachable → one Warning without exception, unexpected exception → Error with exception.
6. **Docs** – add a `### <Name>` section to `README.md` (config example, enable conditions, what is sent,
   units, rate limits) and a section to `docs/specs/weather-forwarding.md`. Update the target list in the
   README intro and in `AGENTS.md`.
7. Run `dotnet build` and `dotnet test`; there must be no new compiler or analyzer warnings.
