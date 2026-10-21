# AGENTS.md

Notes for AI coding agents working on this repository. User-facing documentation is in `README.md`.

## Project

ASP.NET Core (.NET 10) service that receives weather station uploads (WS Link custom server,
`GET/POST /data/upload.php` with query string parameters, protocol in `docs/API.pdf`) and forwards the data
to Home Assistant, Windy, openHAB and Gladys Assistant (MQTT).

| Project                         | Content                                                                    |
|---------------------------------|----------------------------------------------------------------------------|
| `src/WsLink.Api`                | Host, controller, `appsettings.json`, `nlog.config`, version               |
| `src/WsLink.Business`           | `WeatherService`, adapters (`Adapters/`), MQTT publisher (`Mqtt/`), DI     |
| `src/WsLink.Common`             | Contracts (`WsLinkRequestParam`, `WeatherStationData`), config classes, interfaces |
| `tests/WsLink.Business.Tests`   | Unit tests (xUnit v4)                                                      |
| `src/WsLink.DataProvider`       | Empty placeholder, not in the solution                                     |

Flow: `WsLinkDataController` → `WeatherService.SaveWeatherData` (rounding, outdoor sensor handling) →
all enabled `IWeatherAdapter`s in parallel (`Task.WhenAll`). `AdapterFactory` returns enabled adapters,
or `DummyWeatherAdapter` (logs only) when none is enabled.

Behavioral rules (rounding, missing values, timeouts, logging levels) are specified in
`docs/specs/weather-forwarding.md`. Keep code, tests and that spec consistent.

## Commands

```sh
dotnet build
dotnet test                                    # Microsoft Testing Platform, enabled in global.json
dotnet test --filter-class WsLink.Business.Tests.Adapters.WindyWeatherAdapterTests
dotnet run --project src/WsLink.Api --urls http://0.0.0.0:8080
```

- Tests run on Microsoft Testing Platform (MTP v2), not VSTest. VSTest `--filter "..."` syntax does not work,
  use `--filter-class`, `--filter-method`, etc.
- The default `http` launch profile listens on `localhost` only, a station on the network cannot reach it.
- `dotnet` output follows the OS language. On a Czech system: `Počet chyb: 0` = 0 errors, `Úspěšné` = passed,
  `Neúspěšné` = failed.
- `NU1902`/`NU1903` are real vulnerabilities in transitive `System.Security.Cryptography.Xml`/`.Pkcs` packages,
  pulled in by the legacy `Microsoft.AspNetCore.Mvc` 2.3.x package in `WsLink.Common` (used for `[FromQuery]`).
  They are pre-existing, not caused by your change. Fixing them is a separate task (e.g. replace the package with
  `<FrameworkReference Include="Microsoft.AspNetCore.App" />`); mention them, do not silently ignore new ones.

## Code conventions

- Match the surrounding code: file-scoped namespaces, 4 spaces, `ConfigureAwait(false)` in adapters,
  `IHttpClientFactory` with a named client per adapter.
- Comments explain *why* (non-obvious decisions, protocol quirks), not *what*.
- Structured log messages use the format `"Message [Key: {Key}, Other: {Other}]"`.
- Adapter error handling:
  - expected failures (`HttpRequestException`, timeouts) → `LogWarning` with `e.Message`, **no exception object**
    (stack traces would flood the log when a target is down),
  - non-success status → `LogWarning` with the status code,
  - unexpected exceptions → `LogError(e, ...)`,
  - an adapter never throws out of `ProcessWeatherDataAsync`.
- Adapter config validation happens once (constructor / `IsEnabled`); an incomplete config disables the adapter.
- Contract property names must not change their serialized names: `JsonPropertyName` on `WeatherStationData`
  (Home Assistant payload) and `FromQuery(Name = ...)` on `WsLinkRequestParam` (station protocol).

## Tests

- xUnit v4 = NuGet package `xunit.v3` version 4.x (there is no `xunit.v4` package). Test project is `OutputType Exe`.
- **AAA style is required**: every test has `// Arrange`, `// Act`, `// Assert` sections, and the action
  is not hidden inside an assert (`var isEnabled = adapter.IsEnabled;` in Act, then assert it).
- No mocking library. Use the fakes in `tests/WsLink.Business.Tests/Fakes`:
  `FakeHttpMessageHandler` (records requests incl. content, `Returning`/`Throwing` helpers),
  `FakeHttpClientFactory`, `ListLogger<T>` (captures level, message and exception), `FakeWeatherAdapter`,
  `FakeAdapterFactory`, `FakeMqttPublisher`, `CultureScope` (e.g. `cs-CZ` to check invariant number formatting).
- Pass `TestContext.Current.CancellationToken` to methods accepting a `CancellationToken` (analyzer xUnit1051).
- Avoid midpoint inputs in rounding tests (`x.x5`), `Math.Round` uses banker's rounding and binary floats
  are inexact.
- When fixing a bug, verify the new test fails on the old code.

## Configuration and secrets

- Config sources: `appsettings.json`, `appsettings.{Environment}.json`, environment variables (`Section__Key`).
- `appsettings.Development.json` is git-ignored and holds real local settings. Do not print secrets
  from it (mask `WebhookId`, `StationPassword`, `ApiToken`, `StationId`, `Password`).
- Do not read `.env` files, they contain secrets.
- Never commit secrets to `appsettings.json`; it holds only non-secret defaults (`Enabled: false` etc.).

## Docker

- The registry `192.168.88.28:5000` is a private insecure HTTP registry, images are `wslink-api:vX.Y.Z`.
- Server images must be `linux/amd64`. A plain `docker build` / `docker compose build` produces an image for
  the build machine's architecture, i.e. **arm64** on Apple Silicon. Push only with:
  `docker buildx build --platform linux/amd64 -t 192.168.88.28:5000/wslink-api:vX.Y.Z --push .`
- Never `docker compose push` from an arm64 machine: `compose.yaml` has both `build:` and the registry `image:`,
  so it would overwrite the amd64 image with an arm64 one.
- The Dockerfile build stage runs on `$BUILDPLATFORM` and publishes with `-a $TARGETARCH`, because
  the .NET SDK crashes under QEMU emulation. Keep this when editing the Dockerfile.
- Verify a pushed image via the registry API (`/v2/wslink-api/tags/list`, manifest platform) and by running it
  with `--platform linux/amd64` and calling `/health`.

## Git and release workflow

- Work happens directly on `main`, no feature branches or pull requests. Commit or push only when asked.
- Commit messages: short, lowercase summary (e.g. `add openHAB adapter`, `set version 0.1.0`), optional body
  with `-` bullets. Split unrelated changes (e.g. a fix and its tests' infrastructure, Dockerfile vs compose).
- Version is set only in `src/WsLink.Api/WsLink.Api.csproj` (`Version`, `AssemblyVersion`, `FileVersion`).
- Tags are annotated (`vX.Y.Z`). Do not move a pushed tag.
- Full release procedure: `.claude/skills/release/SKILL.md`.

## Communication

The maintainer writes in Czech; answer in Czech. Code, comments, commit messages, README and these docs are in English.
