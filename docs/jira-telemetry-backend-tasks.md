# Backend Jira backlog — Telemetry, Live Monitoring, Incidents, and Route Weather

Source baseline: `Report3_Software Requirement Specification.docx`, especially FE-05/UC-13/FR-13, FE-06/UC-14/FR-14, FE-07/UC-15/FR-15, FE-08/UC-16–18/FR-16–18, BR-07–10, CR-01–07, NFR-R02 and NFR-P02.

Current backend baseline:

- `TelemetryRecord` stores mission/UAV, latitude, longitude, altitude, speed, battery level and one `RecordedAt` timestamp.
- `GET /api/flight-history/{missionId}/telemetry` provides stored playback data only.
- There is no telemetry ingestion gateway, latest-state endpoint, live delivery channel, explicit freshness/connectivity projection, mission progress projection, or weather endpoint.
- Weather is an extension requested for mission planning/monitoring. It is not a field required by FE-05 MAVLink telemetry and must remain distinguishable from measured UAV telemetry.

## Epic: BE — MAVLink Telemetry and Mission Environment Services

**Goal:** ingest authenticated UAV/simulator telemetry, expose an authorized live mission state, detect operational incidents, retain post-flight evidence, and provide forecast weather along the planned route.

**SRS constraints applying to every story:**

- Missing telemetry remains `null`/unavailable and is never converted to zero (FR-13.2).
- Preserve source time and server-received time; expose freshness and missing-data state (BR-07, CR-03).
- Out-of-order samples cannot replace newer live state (FR-13.2).
- Connection loss must be evaluated by a timer even when no packets arrive (BR-08, FR-15.1).
- No API in this epic issues low-level flight-control commands (product scope, BR-05).
- Human reads/actions use record-scope authorization; integrations use dedicated source credentials (BR-01, CR-01).

---

## TEL-01 — Finalize telemetry and incident contracts (D-05/D-06)

**Type:** Technical spike / decision task  
**Priority:** Blocker  
**Labels:** `backend`, `telemetry`, `srs-d05`, `srs-d06`

Decide and document:

- Supported MAVLink messages and mapping. Minimum proposed subset: `HEARTBEAT`, `GLOBAL_POSITION_INT`, `VFR_HUD`, `SYS_STATUS` or `BATTERY_STATUS`, and mission progress messages such as `MISSION_CURRENT`.
- Source identity mapping to UAV, mission/flight association, sample rate, retry and deduplication key.
- `sourceTimestamp`, `receivedAt`, clock-skew tolerance, out-of-order and late-arrival policy.
- Altitude reference and units: MSL/relative/AGL must not be silently mixed.
- Flight start/completion event; disconnect alone must never complete a flight.
- Configurable thresholds, severity, hysteresis, cooldown and duplicate window for communication loss, low battery, altitude and speed incidents.

**Acceptance criteria:**

- A reviewed ADR/SDD section resolves D-05 and D-06.
- Field/unit/nullability table and sample payloads are attached.
- Decisions trace to FR-13–15, BR-07–09 and CR-03.
- No threshold is presented as an aviation regulation unless the team supplies an approved source.

---

## TEL-02 — Implement authenticated MAVLink telemetry ingestion gateway

**Type:** Story  
**Priority:** Highest  
**Depends on:** TEL-01  
**Labels:** `backend`, `mavlink`, `telemetry-ingestion`, `uc-13`

Implement the gateway that accepts configured UAV/autopilot/simulator sources and converts supported packets into normalized samples.

**Proposed internal contract:**

```http
POST /api/integrations/telemetry/samples
Authorization: integration credential
Idempotency-Key: {sourceId}:{messageSequence}:{sourceTimestamp}
```

Normalized sample fields:

```json
{
  "sourceId": "sim-uav-001",
  "uavId": "uuid",
  "missionId": "uuid-or-null",
  "sourceTimestamp": "2026-10-13T10:00:00.000Z",
  "latitude": 10.849,
  "longitude": 106.772,
  "altitudeMeters": 82.4,
  "altitudeReference": "Relative",
  "groundSpeedMetersPerSecond": 12.6,
  "batteryPercent": 74,
  "flightMode": "AUTO",
  "armed": true,
  "missionSequence": 3
}
```

**Acceptance criteria:**

- Reject unknown source credentials and invalid UAV mapping.
- Validate coordinate, battery and numeric ranges server-side.
- Unsupported/malformed packets are logged without crashing the stream.
- Deduplicated retries create one stored sample.
- Missing fields persist as `null`; they do not become zero.
- Out-of-order data may be retained as evidence according to TEL-01 but cannot replace the latest live state.
- No credential or full sensitive payload is written to normal application logs.
- Integration tests cover valid, duplicate, malformed, unknown-source and out-of-order samples.

---

## TEL-03 — Expand telemetry persistence and latest-state projection

**Type:** Story  
**Priority:** Highest  
**Depends on:** TEL-01, TEL-02  
**Labels:** `backend`, `database`, `telemetry`, `br-07`

Extend telemetry storage beyond the current playback fields and maintain one latest valid state per UAV/active flight.

**Required persisted metadata:** source ID, optional mission/flight association, source timestamp, received timestamp, validity/status, latitude/longitude, altitude value/reference, ground speed, battery, flight mode, armed state, mission sequence and a deduplication identifier.

**Acceptance criteria:**

- Database migration preserves existing `TelemetryRecord` rows.
- Indexes support mission playback ordered by source time and latest-state lookup by UAV.
- Latest-state update is atomic and rejects replacement by an older accepted sample.
- Source and received timestamps are returned separately.
- Data retention and late-data behavior are configurable/documented under D-05/D-07.

---

## TEL-04 — Expose authorized live mission state and streaming updates

**Type:** Story  
**Priority:** Highest  
**Depends on:** TEL-03  
**Labels:** `backend`, `live-monitoring`, `websocket`, `uc-14`

Provide the initial snapshot and push accepted updates to authorized Pilot and Operations Manager clients.

**Proposed APIs:**

```http
GET /api/live-monitoring/missions/{missionId}/state
GET /api/live-monitoring/missions
WebSocket or SignalR: /hubs/telemetry
```

Snapshot fields must include location, altitude, speed, battery, connectivity, sample freshness, mission progress, last source timestamp, last received timestamp and linked open incidents.

**Acceptance criteria:**

- Pilot can access only owned/assigned mission scope; Manager can access managed scope.
- State explicitly returns `Live`, `Stale`, `Offline` or `Unavailable` plus `lastUpdatedAt`.
- Last-known values remain visible when stale but are labelled stale/offline.
- Reconnect immediately supplies the newest valid snapshot before incremental events.
- Mission progress is based on agreed mission sequence/waypoint evidence and is `null` when unavailable.
- Under SRS profile P-01, 95% of accepted samples reach an authorized live client within 2 seconds (NFR-P02).
- Tests verify scope denial, stale transition and reconnect behavior.

---

## TEL-05 — Implement telemetry freshness and connectivity watchdog

**Type:** Story  
**Priority:** High  
**Depends on:** TEL-01, TEL-03  
**Labels:** `backend`, `connectivity`, `scheduler`, `br-08`

Run a background check independently of packet arrival to classify telemetry freshness and detect communication loss.

**Acceptance criteria:**

- Thresholds come from configuration resolved in TEL-01, not constants scattered through code.
- A stream becomes stale/offline within the configured threshold plus 2 seconds (NFR-R02).
- Reconnect data restores live state within 5 seconds after acceptance (NFR-R02).
- Disconnect alone does not mark a mission Completed.
- State changes are idempotent and auditable.
- Timer tests use a controllable clock and do not rely on real waiting.

---

## TEL-06 — Detect and persist operational incidents from telemetry

**Type:** Story  
**Priority:** High  
**Depends on:** TEL-01, TEL-03, TEL-05  
**Labels:** `backend`, `incidents`, `uc-15`, `fr-15`

Evaluate configured rules for communication loss, low battery, abnormal altitude and abnormal speed. Persist incident evidence and publish an in-app alert.

**Acceptance criteria:**

- Incident records contain type, severity, UAV/mission, event/source time, detection time, threshold/rule version and linked sample evidence.
- Missing altitude suppresses only the altitude rule; other rules continue.
- Hysteresis/cooldown prevents one persistent condition from flooding duplicate incidents.
- Reconnect closes or updates communication-loss state according to TEL-01 without deleting history.
- Incident creation never changes measured telemetry.
- Alerts are delivered only to authorized recipients and delivery failure does not delete the incident.

---

## TEL-07 — Complete incident list, detail and lifecycle APIs

**Type:** Story  
**Priority:** High  
**Depends on:** TEL-06  
**Labels:** `backend`, `incidents`, `uc-16`, `scr-11`, `scr-12`

The current backend exposes incidents only under `GET /api/flight-history/{missionId}/incidents`. Add operational APIs required by the SRS screens.

**Proposed APIs:**

```http
GET  /api/incidents?missionId=&uavId=&type=&severity=&status=&from=&to=&pageNumber=&pageSize=
GET  /api/incidents/{incidentId}
POST /api/incidents/{incidentId}/acknowledge
POST /api/incidents/{incidentId}/resolve
```

**Acceptance criteria:**

- Detail includes linked telemetry evidence and append-only status history.
- Assigned Pilot/Manager acknowledgement and Manager resolution follow the approved permission matrix.
- Every transition records actor, time and note.
- Invalid/duplicate/concurrent transitions return a conflict response and do not overwrite history.
- Missing evidence is explicitly labelled unavailable.

---

## TEL-08 — Upgrade telemetry playback and post-flight evidence APIs

**Type:** Story  
**Priority:** Medium  
**Depends on:** TEL-03  
**Labels:** `backend`, `post-flight`, `playback`, `uc-17`, `uc-18`

Extend `GET /api/flight-history/{missionId}/telemetry` so long flights remain usable and incomplete data is disclosed.

**Proposed query:**

```http
GET /api/flight-history/{missionId}/telemetry?from=&to=&maxPoints=&cursor=
```

**Acceptance criteria:**

- Results are ordered deterministically and include source/received timestamps and validity.
- Pagination or server-side downsampling prevents unbounded responses.
- Response exposes completeness, sample count, first/last sample time and missing-data gaps.
- Planned versus recorded route can be reconstructed where coordinates exist.
- Existing clients using the current fields remain compatible or receive a versioned endpoint.
- Post-flight aggregation ignores null measurements instead of treating them as zero.

---

## WTH-01 — Integrate forecast provider for mission waypoint weather

**Type:** Story / SRS change request  
**Priority:** High  
**Depends on:** Mission detail with ordered waypoints  
**Labels:** `backend`, `weather`, `waypoints`, `external-api`

This story extends the SRS. Weather must be stored/labelled as provider forecast, not UAV telemetry. Use a server-side provider adapter; Open-Meteo is the proposed first implementation, with provider configuration replaceable later.

For every representative waypoint and estimated arrival time, request and normalize:

- Temperature at 2 m (`temperatureCelsius`).
- Relative humidity at 2 m (`relativeHumidityPercent`).
- Wind speed and direction; use altitude-level wind when the provider supports a suitable level, otherwise disclose 10 m surface wind.
- Wind gust at 10 m.
- Precipitation probability, precipitation amount and weather code/type.
- Visibility.
- Cloud cover; optionally cloud base/ceiling when supported by the selected provider.
- Forecast timestamp, provider, model/run metadata when available and retrieval time.

**Acceptance criteria:**

- Provider secrets/config remain server-side.
- Mission coordinates and scheduled start/end time determine forecast points/times.
- Backend estimates waypoint arrival time or marks it unavailable; it does not use one current-weather value for the entire route.
- Nearby waypoints/time buckets are deduplicated and cached to control provider usage.
- Provider timeout/rate limit returns an explicit unavailable/partial result and does not block mission details.
- Every value includes units and forecast time; unavailable fields are `null`.
- Tests use a deterministic provider stub and cover full, partial, timeout and out-of-forecast-window responses.

---

## WTH-02 — Expose route-weather summary and per-waypoint risk API

**Type:** Story / SRS change request  
**Priority:** High  
**Depends on:** WTH-01, TEL-01 for approved configurable limits  
**Labels:** `backend`, `weather`, `mission-planning`, `dashboard`

**Proposed API:**

```http
GET /api/missions/{missionId}/weather
POST /api/missions/{missionId}/weather/refresh
```

**Minimum response:** provider/retrieval time, mission flight window, overall availability/risk, and ordered waypoint results containing coordinates, altitude, estimated arrival time, temperature, humidity, wind/gust, precipitation, visibility, cloud cover, weather code and warnings.

**Acceptance criteria:**

- Response distinguishes `Available`, `Partial`, `Unavailable` and `Stale`.
- Overall risk is explainable from configurable operational limits; no invented legal threshold.
- Each warning identifies waypoint, measured forecast value, threshold and unit.
- A cached response is returned with `generatedAt`/`expiresAt`; authorized refresh is rate-limited.
- Pilot access is limited to owned/assigned missions; Manager follows managed scope.
- Changing route or flight time invalidates the corresponding cached route-weather assessment.

---

## TEL-09 — Add telemetry/weather observability and SRS acceptance tests

**Type:** Task  
**Priority:** High  
**Depends on:** TEL-02–08, WTH-01–02  
**Labels:** `backend`, `observability`, `testing`, `nfr`

**Acceptance criteria:**

- Metrics cover accepted/rejected/duplicate/out-of-order samples, ingestion-to-publish latency, connected sources, stale/offline UAVs, incident counts, provider calls/cache hits/errors and weather latency.
- Structured logs include correlation/source/UAV/mission identifiers without credentials.
- Load test uses SRS P-01: 5 simulated UAVs at 1 normalized sample/second, 10 concurrent Web sessions, and stored flights of up to 10,000 samples.
- Demonstrate NFR-P02 and NFR-R02 with recorded test evidence.
- Failure tests prove stored telemetry/incidents survive stream, notification and report-generation failures.

## Recommended delivery order

1. TEL-01
2. TEL-02 and TEL-03
3. TEL-04 and TEL-05
4. TEL-06 and TEL-07
5. TEL-08
6. WTH-01 and WTH-02
7. TEL-09

