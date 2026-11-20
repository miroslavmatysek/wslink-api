-- Table for the Postgres adapter of WsLink.Api (TimescaleDB hypertable).
-- Run once in the target database as a user allowed to create extensions, e.g.
--   psql -h <host> -U <user> -d wslink -f docs/sql/timescaledb.sql
-- The compose.yaml postgres service runs it automatically when its data volume is initialized.
-- Column names match the JsonPropertyName of WeatherStationData and the INSERT in DapperWeatherDataProvider.

CREATE EXTENSION IF NOT EXISTS timescaledb;

CREATE TABLE IF NOT EXISTS weather_data (
    time                 timestamptz      NOT NULL, -- time of receipt by the service (UTC)
    out_sensor_connected boolean          NOT NULL,
    out_temp             double precision,          -- °C
    out_hum              double precision,          -- %
    dew_point            double precision,          -- °C
    in_temp              double precision,          -- °C
    in_hum               double precision,          -- %
    a_pressure           double precision,          -- hPa
    r_pressure           double precision,          -- hPa
    wind_direction       integer,                   -- °
    wind_speed           double precision,          -- m/s
    wind_speed_10min     double precision,          -- m/s
    wind_gust            double precision,          -- m/s
    rain_rate            double precision,          -- mm/h
    rainfall_hourly      double precision,          -- mm
    rainfall_daily       double precision,          -- mm
    rainfall_weekly      double precision,          -- mm
    rainfall_monthly     double precision,          -- mm
    rainfall_yearly      double precision,          -- mm
    uvi                  double precision,
    light_intensity      double precision           -- W/m²
);

SELECT create_hypertable('weather_data', by_range('time'), if_not_exists => TRUE);
