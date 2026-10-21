# WsLink.Api

Small ASP.NET Core service that receives data from a weather station using the WS Link "custom server" upload
and forwards it to [Home Assistant](#home-assistant), [Windy](#windy) and [openHAB](#openhab).

How it works:

1. The station calls `GET` or `POST /data/upload.php?...` with the measured values in the query string
   (see `docs/API.pdf` for the station protocol).
2. Values are rounded to one decimal place. Non-finite values (e.g. `NaN`) are treated as missing.
3. When the outdoor sensor is disconnected (`t1cn=0`), all outdoor values are dropped, so no stale or zero values
   are forwarded. Indoor temperature, indoor humidity and pressure are still sent.
4. The data is sent to all enabled targets in parallel. A failing or slow target does not affect the others,
   each target is limited to 10 s. When no target is enabled, the data is only logged.

## Endpoints

| Endpoint                    | Description                                      |
|-----------------------------|--------------------------------------------------|
| `GET/POST /data/upload.php` | Weather station upload (query string parameters) |
| `GET /health`               | Health check                                     |

Example requests are in `src/WsLink.Api/WsLink.Api.http`.

## Configuration

Configuration is read from `appsettings.json`, `appsettings.{Environment}.json` and environment variables.
Nested keys use `__` in environment variables, e.g. `HomeAssistant__WebhookId=...`.

Keep secrets (webhook id, Windy station password, openHAB API token) out of the committed `appsettings.json`:

- In Docker, pass them as environment variables. The image runs in the `Production` environment,
  so `appsettings.Development.json` is not loaded there. See [Docker](#docker).
- For local development, `appsettings.Development.json` (ignored by git) can be used as well. It is loaded only
  when `ASPNETCORE_ENVIRONMENT=Development`, which the launch profiles in `launchSettings.json` set.

### Home Assistant

Weather data is sent as JSON to a Home Assistant [webhook](https://www.home-assistant.io/docs/automation/trigger/#webhook-trigger)
(`POST {BaseUrl}/api/webhook/{WebhookId}`).

```json
"HomeAssistant": {
  "Enabled": true,
  "BaseUrl": "http://homeassistant.local:8123",
  "WebhookId": "<webhook id>"
}
```

- The adapter is enabled only when `Enabled` is `true` and `WebhookId` is set.
- `BaseUrl` defaults to `http://homeassistant.local:8123`. It can contain a sub-path (Home Assistant behind
  a reverse proxy), e.g. `http://proxy.local/ha` sends to `http://proxy.local/ha/api/webhook/{WebhookId}`.
- Missing values are not included in the payload at all. Payload properties:
  `out_sensor_connected`, `out_temp`, `out_hum`, `in_temp`, `in_hum`, `a_pressure`, `r_pressure`, `wind_direction`,
  `wind_speed`, `wind_speed_10min`, `wind_gust`, `rain_rate`, `rainfall_hourly`, `rainfall_daily`, `rainfall_weekly`,
  `rainfall_monthly`, `rainfall_yearly`, `uvi`, `light_intensity`.
- Units are the station units: °C, %, hPa, °, m/s, mm/h, mm, W/m².

### Windy

Observations are sent to the [Windy stations API](https://stations.windy.com)
(`GET {ApiHost}/api/v2/observation/update`).

```json
"Windy": {
  "Enabled": true,
  "ApiHost": "https://stations.windy.com",
  "StationId": "<station id>",
  "StationPassword": "<station password>",
  "UpdateInterval": "00:05:00"
}
```

- The adapter is enabled only when `Enabled` is `true` and `ApiHost`, `StationId` and `StationPassword` are set.
- `ApiHost` defaults to `https://stations.windy.com`, `UpdateInterval` defaults to 5 minutes.
- At most one update is sent per `UpdateInterval`, station posts in between are skipped. A failed update
  (Windy unreachable, error response like `429`) is not retried before the interval elapses.
- Sent values: temperature, humidity, relative pressure, wind speed, gust and direction, UV index, solar radiation
  and hourly rainfall. Missing values are not sent.

### openHAB

Weather data can be sent to openHAB items via the REST API (`PUT /rest/items/{item}/state`).

```json
"OpenHab": {
  "Enabled": true,
  "BaseUrl": "http://openhab.local:8080",
  "ApiToken": "<openHAB API token>",
  "Items": {
    "OutTemperature": "WsLink_OutTemperature",
    "OutSensorConnected": "WsLink_OutSensorConnected"
  }
}
```

- `Items` maps a `WeatherStationData` property name to an openHAB item name. Only mapped values are sent, there is no default mapping.
  Available properties: `OutSensorConnected`, `OutTemperature`, `OutHumidity`, `InTemperature`, `InHumidity`,
  `AbsolutePressure`, `RelativePressure`, `WindDirection`, `WindSpeed`, `WindSpeed10MinutesAvg`, `WindGust`,
  `RainRate`, `HourlyRainfall`, `DailyRainfall`, `WeeklyRainfall`, `MonthlyRainfall`, `YearlyRainfall`, `Uvi`, `LightIntensity`.
- Values are sent with units (°C, hPa, °, m/s, mm/h, mm, W/m²), so dimensioned items (e.g. `Number:Speed`) convert them
  to their own unit and plain `Number` items ignore the unit. Humidity and UV index are sent without unit; for humidity
  use a plain `Number` item or `Number:Dimensionless` with `unit="%"` metadata. `OutSensorConnected` is sent as `ON`/`OFF` (Switch item).
- Missing values (e.g. disconnected outdoor sensor) are sent as `UNDEF`.
- `BaseUrl` can contain a sub-path (openHAB behind a reverse proxy). An invalid `BaseUrl` or no valid item mapping
  disables the adapter and logs an error on the first station upload.
- `ApiToken` is optional when the openHAB REST API allows anonymous access.
- Items are updated with at most 4 parallel requests, the whole update is limited to 10 s.
  Failures are logged as a single warning per update (with the first error), details are logged on Debug level.
- Configuration can be set via environment variables, e.g. `OpenHab__Items__OutTemperature=WsLink_OutTemperature`.

## Running

Requires the .NET 10 SDK.

```sh
dotnet run --project src/WsLink.Api --urls http://0.0.0.0:8080
```

The service listens on port `8080` on all network interfaces. Point the station's custom server upload to
this machine's address and port. Without `--urls`, the default `http` launch profile listens on `localhost` only,
which is enough for local testing (e.g. with `WsLink.Api.http`), but the station cannot reach it.

### Docker

```sh
docker compose up -d --build
```

`compose.yaml` runs the API on port `8080` and a [Caddy](https://caddyserver.com) reverse proxy with an internal
TLS certificate on port `4443`. Adjust the image registry and the Caddy host address to your environment.

Pass the configuration (including secrets) as environment variables of the `wslink.api` service,
e.g. in `compose.yaml` or in a `.env` file next to it (keep it out of git):

```yaml
services:
  wslink.api:
    environment:
      HomeAssistant__Enabled: "true"
      HomeAssistant__BaseUrl: "http://homeassistant.local:8123"
      HomeAssistant__WebhookId: "${HA_WEBHOOK_ID}"
      Windy__Enabled: "true"
      Windy__StationId: "${WINDY_STATION_ID}"
      Windy__StationPassword: "${WINDY_STATION_PASSWORD}"
```

## Logging

Logging uses NLog (`src/WsLink.Api/nlog.config`), from Debug level up. Logs are written to `Logs/`:

- `Wslink.Api-*.log` and the console: application logs, without `Microsoft.*` logs and without Debug/Info
  `HttpClient` request logs.
- `Wslink.Api-all-*.log`: everything, including framework logs.

Unreachable targets are logged as warnings without stack traces, so an outage of one target does not flood the log.

## Tests

Unit tests are in `tests/WsLink.Business.Tests` (xUnit v4 on Microsoft Testing Platform, enabled in `global.json`).

```sh
dotnet test
```
