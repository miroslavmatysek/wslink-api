# Spec: weather data forwarding

Behavioral rules of the service. Unit tests in `tests/WsLink.Business.Tests` encode these rules;
a change of behavior must update this spec, the tests and `README.md` together.

## 1. Station input → `WeatherStationData` (`WeatherService`)

1. All `double` values are rounded to one decimal place (`Math.Round(value, 1)`).
2. Non-finite input values (`NaN`, `±Infinity`) become `null` (missing).
3. A value rounded to zero from below is `0`, never `-0`.
4. `OutSensorConnected = t1cn != 0`. A missing `t1cn` means connected.
5. When the outdoor sensor is disconnected, all outdoor values are `null`: temperature, humidity, dew point (`t1dew`),
   wind (direction, speed, 10 min average, gust), rain (rate, hourly, daily, weekly, monthly, yearly),
   UV index and light intensity.
   Indoor temperature, indoor humidity and both pressures are kept.
6. A change of the outdoor sensor state is logged once (Information when connected, Warning when disconnected),
   not on every upload.
7. The same `WeatherStationData` instance is passed to all enabled adapters, which run in parallel.
   One slow or failing adapter must not block the others.

## 2. Adapter contract (`IWeatherAdapter`)

1. `IsEnabled` is evaluated from configuration; an incomplete configuration means disabled
   (never "enabled but failing on every upload").
2. `AdapterFactory` returns only enabled adapters, or `DummyWeatherAdapter` when none is enabled.
3. `ProcessWeatherDataAsync` never throws.
4. Every adapter finishes within 10 s (HttpClient timeout, or its own overall timeout).
5. Logging:

   | Situation                                      | Level       | Exception object |
   |------------------------------------------------|-------------|------------------|
   | Success                                        | Information | –                |
   | Non-success HTTP status                        | Warning     | no               |
   | Target unreachable / timeout                   | Warning     | no               |
   | Unexpected exception                           | Error       | yes              |

   A down target produces at most one Warning per upload.
6. Secrets are not logged: the station password in request logs is masked by `nlog.config` (`wspw=***`,
   `${regex-replace}` from `NLog.RegEx`), Windy station id is masked, tokens are only in headers, the MQTT password is never logged.
7. Numbers sent as text use `CultureInfo.InvariantCulture`.

## 3. Home Assistant

- Enabled when `Enabled` and `WebhookId` is not blank.
- `POST {BaseUrl}/api/webhook/{WebhookId}`, `BaseUrl` default `http://homeassistant.local:8123`.
  A sub-path of `BaseUrl` is kept; a `BaseUrl` without scheme defaults to `http`.
- Body: JSON of `WeatherStationData` using its `JsonPropertyName`s; `null` values are omitted.

## 4. Windy

- Enabled when `Enabled` and `ApiHost`, `StationId`, `StationPassword` are not blank.
- `GET {ApiHost}/api/v2/observation/update?id={StationId}&...` with `Authorization: Bearer {StationPassword}`.
- Query parameters: `temp` (out temperature), `humidity` (out humidity), `dewpoint`, `mbar` (relative pressure),
  `wind`, `gust`, `winddir`, `uv`, `solarradiation` (light intensity), `precip` (hourly rainfall).
  Doubles are formatted `F1`, integers as is. Missing values are not sent.
- At most one request per `UpdateInterval` (default 5 min, Windy accepts at most one update per 5 min).
  The interval starts when a request is attempted, so a failed request (unreachable, `429`, …) is not retried
  before the interval elapses. It is measured in UTC, so a daylight saving time change does not affect it.
- The station id is not logged (Debug log of the query has `id=masked`), the password is only in the header.

## 5. openHAB

- Enabled when `Enabled`, `BaseUrl` is an absolute `http`/`https` URL and at least one valid item mapping exists.
  Invalid `BaseUrl` or no valid mapping → disabled and an Error logged when the adapter is created
  (adapters are singletons created with `WeatherService`, i.e. on the first station upload, not at startup).
- `Items` maps a `WeatherStationData` property name (case-insensitive) to an item name. Unknown property
  → Warning, ignored. Item names are trimmed (a stray space from env variables would cause 404). Blank item name → ignored. Empty `Items` → default mapping of every property to
  `WsLink_{Property}` (Information logged). A non-empty `Items` replaces the default, it is not merged with it;
  the default is built in the adapter, not in `appsettings.json`, because configuration merges dictionaries per key.
- `PUT {BaseUrl}/rest/items/{item}/state`, body `text/plain`, optional `Authorization: Bearer {ApiToken}`
  (token trimmed). A sub-path of `BaseUrl` is kept.
- State format:

  | Property                                               | State           |
  |--------------------------------------------------------|-----------------|
  | `OutSensorConnected`                                   | `ON` / `OFF`    |
  | `OutTemperature`, `InTemperature`, `DewPoint`          | `<v> °C`        |
  | `OutHumidity`, `InHumidity`, `Uvi`                     | `<v>` (no unit) |
  | `AbsolutePressure`, `RelativePressure`                 | `<v> hPa`       |
  | `WindDirection`                                        | `<v> °`         |
  | `WindSpeed`, `WindSpeed10MinutesAvg`, `WindGust`       | `<v> m/s`       |
  | `RainRate`                                             | `<v> mm/h`      |
  | `HourlyRainfall` … `YearlyRainfall`                    | `<v> mm`        |
  | `LightIntensity`                                       | `<v> W/m²`      |
  | any missing value                                      | `UNDEF`         |

  Humidity has no `%`, because `Number:Dimensionless` would store `45 %` as `0.45`.
- At most 4 parallel requests, overall timeout 10 s for the whole update.
- Per-item failures are logged on Debug; one summary per upload: Information when all items were updated,
  otherwise a Warning with failed/total counts, the first error and whether the timeout was hit.

## 6. Gladys Assistant (MQTT)

- Enabled when `Enabled`, `Host` is a host name or IP address (`Uri.CheckHostName`, so no URL or port), `Port`
  is 1–65535, `DeviceExternalId` is valid and at least one valid feature mapping exists. Otherwise disabled and
  an Error logged when the adapter is created (i.e. on the first station upload, like openHAB).
  A `Password` without `Username` is not used and a Warning is logged.
- A valid external id starts with `mqtt:`, has at least one character after it and contains no whitespace,
  `/`, `+`, `#` (it is a single MQTT topic level). Ids are trimmed.
- `Features` maps a `WeatherStationData` property name (case-insensitive) to a feature external id. Unknown property
  or invalid id → Warning, ignored. Blank id → ignored. Empty `Features` → default mapping of every property to
  `{DeviceExternalId}:{JsonPropertyName}` (Information logged); a non-empty `Features` replaces the default.
- Each value is published to `gladys/master/device/{DeviceExternalId}/feature/{FeatureExternalId}/state`,
  QoS 0, not retained. Payload: number formatted with `CultureInfo.InvariantCulture`, `OutSensorConnected` as `1`/`0`.
  Missing values are not published; when no mapped value is present, nothing is published.
- One connection per upload (connect, publish all, disconnect), clean session, client id `{ClientId}-{8 hex}`
  (default `ClientId` is `wslink-api`; the random suffix prevents session takeover between overlapping connections),
  optional username/password (password trimmed). Overall timeout 10 s.
- A failed disconnect after all messages were published is ignored, the update counts as successful.
- MQTT communication errors (refused, bad credentials, timeout), socket and I/O errors and the timeout → one Warning
  with the message; other exceptions → Error with the exception.
