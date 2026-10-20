# KAN-43 — Live telemetry contract examples

All numeric telemetry fields are nullable. A missing source value is JSON `null`, never an invented zero. `boardTemperatureCelsius` is autopilot/electronics temperature; it is not ambient temperature. Until a confirmed environmental sensor is integrated, `environmentalTemperatureCelsius` and `relativeHumidityPercent` remain `null` with `environmentalSensorStatus: "Unavailable"`.

## Missing

```json
{
  "missionId": "00000000-0000-0000-0000-000000000001",
  "sourceTimestamp": null,
  "receivedAt": null,
  "latitude": null,
  "longitude": null,
  "headingDegrees": null,
  "batteryVoltageVolts": null,
  "environmentalTemperatureCelsius": null,
  "relativeHumidityPercent": null,
  "environmentalSensorStatus": "Unavailable",
  "connectivityStatus": "Offline",
  "snapshotKind": "Missing",
  "isStale": true,
  "isValid": false
}
```

## Latest / Online

```json
{
  "sourceId": "sim-uav-001",
  "sourceTimestamp": "2026-10-20T07:00:00Z",
  "receivedAt": "2026-10-20T07:00:00.120Z",
  "latitude": 10.849,
  "longitude": 106.772,
  "altitudeMeters": 82.4,
  "altitudeReference": "Relative",
  "groundSpeedMetersPerSecond": 12.6,
  "headingDegrees": 215.5,
  "batteryPercent": 74,
  "batteryVoltageVolts": 15.8,
  "boardTemperatureCelsius": 47.2,
  "environmentalTemperatureCelsius": null,
  "relativeHumidityPercent": null,
  "environmentalSensorStatus": "Unavailable",
  "connectivityStatus": "Online",
  "snapshotKind": "Latest",
  "isStale": false,
  "isValid": true
}
```

## Stale

The last-known telemetry values remain present, but `dataAgeSeconds` exceeds `TelemetryMonitoring:StaleAfterSeconds`:

```json
{
  "connectivityStatus": "Stale",
  "snapshotKind": "Stale",
  "dataAgeSeconds": 18.4,
  "isStale": true,
  "isValid": true
}
```

## Offline

The last-known values still remain present and must not change mission status to `Completed`:

```json
{
  "connectivityStatus": "Offline",
  "snapshotKind": "Offline",
  "dataAgeSeconds": 34.1,
  "isStale": true,
  "isValid": true
}
```

SSE sends `telemetryUpdated` for a new valid sample and for freshness transitions even when no packet arrives. On reconnect it sends the current snapshot first. Duplicate, late and clock-skew-invalid samples never replace a newer valid projection.
