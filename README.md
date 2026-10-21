# WsLink.Api

Small ASP.NET Core service that receives data from a weather station using the WS Link "custom server" upload
and forwards it to [Home Assistant](#home-assistant), [Windy](#windy), [openHAB](#openhab) and
[Gladys Assistant](#gladys-assistant).

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

Keep secrets (webhook id, Windy station password, openHAB API token, MQTT password) out of the committed `appsettings.json`:

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
  `out_sensor_connected`, `out_temp`, `out_hum`, `dew_point`, `in_temp`, `in_hum`, `a_pressure`, `r_pressure`, `wind_direction`,
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
- Sent values: temperature, humidity, dew point, relative pressure, wind speed, gust and direction, UV index,
  solar radiation and hourly rainfall. Missing values are not sent.
- The station id is not logged, the logged query contains `id=masked`.

### openHAB

Weather data can be sent to openHAB items via the REST API (`PUT /rest/items/{item}/state`).

```json
"OpenHab": {
  "Enabled": true,
  "BaseUrl": "http://openhab.local:8080",
  "ApiToken": "<openHAB API token>",
  "Items": {
    "OutTemperature": "Garden_Temperature",
    "OutSensorConnected": "Garden_WeatherSensor"
  }
}
```

- `Items` maps a `WeatherStationData` property name to an openHAB item name. Only mapped values are sent.
  Without `Items`, every property is sent to an item named `WsLink_<Property>` (e.g. `WsLink_OutTemperature`),
  so with these item names only `Enabled`, `BaseUrl` and `ApiToken` need to be set. A configured `Items` replaces
  the default mapping as a whole. A new version can add a property to the default mapping (e.g. `DewPoint`);
  create its item, otherwise every update logs a warning with `404 NotFound` for it.
  Available properties: `OutSensorConnected`, `OutTemperature`, `OutHumidity`, `DewPoint`, `InTemperature`, `InHumidity`,
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
- Configuration can be set via environment variables, e.g. `OpenHab__Items__OutTemperature=Garden_Temperature`.

### Gladys Assistant

Gladys has no HTTP endpoint for sensor values, so the data is published to the MQTT broker used by the Gladys
[MQTT integration](https://gladysassistant.com/docs/integrations/mqtt/)
(topic `gladys/master/device/{DeviceExternalId}/feature/{FeatureExternalId}/state`, payload is the value).

```json
"Gladys": {
  "Enabled": true,
  "Host": "mqtt.local",
  "Port": 1883,
  "Username": "<MQTT user>",
  "Password": "<MQTT password>",
  "DeviceExternalId": "mqtt:wslink",
  "Features": {
    "OutTemperature": "mqtt:wslink:out_temp"
  }
}
```

- In Gladys, create a device in the MQTT integration with the external id `DeviceExternalId` (default `mqtt:wslink`)
  and a feature for every value you want, e.g. external id `mqtt:wslink:out_temp`, category temperature sensor.
- The adapter is enabled when `Enabled` is `true`, `Host` is a host name or IP address (no `mqtt://`, no port), `Port` is valid and `DeviceExternalId` and at least
  one feature id are valid. External ids must start with `mqtt:` (Gladys requirement) and must not contain whitespace,
  `/`, `+` or `#`. An invalid configuration disables the adapter and logs an error on the first station upload.
- `Features` maps a `WeatherStationData` property name (same names as for [openHAB](#openhab)) to a Gladys feature
  external id. Without `Features`, every property is sent to `{DeviceExternalId}:{payload name}` using the
  [Home Assistant](#home-assistant) payload names, e.g. `mqtt:wslink:out_temp`, `mqtt:wslink:r_pressure`.
  A configured `Features` replaces the default mapping as a whole. States for features that do not exist in Gladys
  are ignored by Gladys.
- Values are plain numbers in the station units (°C, %, hPa, °, m/s, mm/h, mm, W/m²); `OutSensorConnected` is `1`/`0`.
  Missing values (e.g. disconnected outdoor sensor) are not sent, Gladys keeps the last state.
- `Username` and `Password` are optional (anonymous broker). A `Password` without `Username` is not used (warning logged).
- `ClientId` defaults to `wslink-api`. A random suffix is added to every connection (e.g. `wslink-api-1a2b3c4d`),
  so overlapping uploads or several service instances on one broker do not disconnect each other.
  Keep a custom `ClientId` at most 14 characters, some brokers reject client ids longer than 23 characters.
- Every upload opens a new connection, publishes all values (QoS 0, not retained) and disconnects.
  The whole update is limited to 10 s, a failure is logged as a single warning.
- Configuration can be set via environment variables, e.g. `Gladys__Host=mqtt.local`, `Gladys__Password=...`.

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

`compose.yaml` enables openHAB with the default item mapping. It requires `OPENHAB_BASE_URL` (every `docker compose`
command fails without it) and optionally `OPENHAB_API_TOKEN`, e.g. in a `.env` file next to it (ignored by git):

```env
OPENHAB_BASE_URL=http://openhab.local:8080
OPENHAB_API_TOKEN=<openHAB API token>
```

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
- `Wslink.Api-all-*.log`: everything, including framework logs. The station sends its password in the query string,
  so it is masked in the request logs (`wspw=***`).

Unreachable targets are logged as warnings without stack traces, so an outage of one target does not flood the log.

## Tests

Unit tests are in `tests/WsLink.Business.Tests` (xUnit v4 on Microsoft Testing Platform, enabled in `global.json`).

```sh
dotnet test
```
