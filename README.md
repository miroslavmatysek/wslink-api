# WsLink.Api

## openHAB

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
- `ApiToken` is optional when the openHAB REST API allows anonymous access.
- Configuration can be set via environment variables, e.g. `OpenHab__Items__OutTemperature=WsLink_OutTemperature`.
