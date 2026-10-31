# Mission validation workflow

## Behavior

- `POST /api/missions` validates a proposed plan. Mandatory failure returns HTTP 400
  with `message` and `validation` findings; no mission enters the approval queue.
  The failed attempt and plan snapshot are still persisted with no MissionId.
- Passing creation persists the mission, waypoints and validation atomically with
  status `Pending Review`. There is no separate frontend submit step.
- `PUT /api/missions/{id}` validates a proposed edit before replacing anything.
  Failed edits preserve the saved plan. Passing edits create a separate pending
  `MissionUpdateRequest`, including baseline/proposed snapshots and validation.
  Owned Approved missions may receive proposals; the approved plan stays active
  until the manager approves the proposal. Rejecting it preserves that plan and
  Approved status. Executing/completed missions cannot be edited.
- Approval applies the proposed content atomically to the same mission. Request IDs
  and canonical plan hashes provide concurrency checks; there are no fake versions.
  Clients retain `operationId` when retrying an unchanged create/edit payload.
- `GET /api/missions/{id}/history` returns request snapshots and review decisions.
  `GET /api/missions/{id}/validation?updateRequestId=...` scopes evidence to a proposal.
  Revalidation accepts the same optional request ID. Operators can only access
  their own mission evidence; managers can review accessible missions.
- `POST /api/missions/validate` accepts `{ "missionId": "..." }`. It validates the
  saved plan, ignoring any supplied waypoint replacements. Operators are scoped to
  their own mission; managers can revalidate reviewed missions.
- Pending-approval responses and mission detail include the latest stored result,
  timestamp, blocking issues and advisory warnings. No result is shown as Pass
  merely because waypoints exist.
- `POST /api/missions/{id}/approve` always performs fresh checks in its serializable
  retryable decision transaction. Failed results are saved, but no approval/audit
  decision is written. The prior mission status remains. The response records the
  actual `validationRunId`. When the caller provides a reviewed validationRunId,
  the backend also rejects a plan that changed since that snapshot.
- Existing rejection and no-self-approval rules remain.

## Mandatory checks

Required plan/route fields, coordinate and sequence validity, future schedule,
operator ownership of an active UAV, pilot license valid now and throughout the
scheduled flight, and conflicts with an approved or executing UAV/operator booking.
All route nodes **and segments** are checked for intersections with configured
prohibited/restricted airspace, matching the airspace registry and frontend labels.
Entering or touching a restricted boundary blocks validation. Missing/malformed
restriction data blocks validation; bounding-box overlap alone never establishes a restriction hit. Nodes and segments
are checked against polygon boundaries and even-odd filled areas, matching the map.
Invalid topology still produces Incomplete when the submitted route intersects that
area or touches its boundary. Defects elsewhere in a large polygon do not block
a disjoint route. This does not repair or certify the restriction source.
This corrects the older telemetry convention that treated these polygons as allowed
operating areas; telemetry monitoring is not changed by this workflow patch.
`OperationalBoundary` polygons are evaluated as keep-in areas (boundary touch is
allowed); the complete route and every segment must remain within at least one valid
boundary. `Restricted` and `NoFly` polygons are keep-out areas and take precedence:
touching either one still fails even when the route is inside an operational boundary.
Restrictions have immutable revisions and explicit vertical rules: AllAltitudes,
Band, or Unspecified. Bands compare waypoint/segment altitude against their bounds
only where the route intersects the horizontal region. Missing or mismatched
altitude references, unavailable terrain needed for AGL/MSL conversion, or unspecified
vertical rules in an intersected region yield Incomplete evidence. A configured terrain
provider converts AGL and MSL at each affected waypoint and segment boundary; the
fail-closed default never guesses elevation. Restriction changes make prior evidence Stale.
`GET /api/geofences/{id}/revisions` exposes the recorded revision history.
No legal altitude/wind limit is inferred. Degenerate/self-intersecting geometry and
antimeridian crossings in the affected route area require data review.

## Weather is advisory

Open-Meteo hourly forecast data is fetched at route nodes for the current hour and
planned flight interval, in UTC, with 10m wind and gusts in m/s, rain in mm/hour and
WMO weather codes 95/96/99 for thunderstorms. Results are forecasts at surface model
levels, not measured conditions at UAV flight altitude. Partial coverage, dates
outside the provider's forecast horizon, timeouts and missing data produce warnings;
**none blocks approval or departure**. Total weather work has a 12-second budget.

Configure team-approved advisory thresholds on the backend:

```json
{
  "MissionWeather": {
    "WindWarningMetersPerSecond": null,
    "GustWarningMetersPerSecond": null,
    "RainWarningMillimetersPerHour": null
  }
}
```

Null wind/gust thresholds produce a "not configured" warning plus available metrics,
so the application never claims that unconfigured wind limits passed. With no rain
threshold, any forecast rain is flagged. Thunderstorms always generate an advisory.
These settings are advisory thresholds, not certified aircraft operating limits.
Forecast source/docs: https://open-meteo.com/en/docs (provide attribution in the UI).

Weather evidence is available through `GET /api/missions/{id}/weather` and explicit
`POST /api/missions/{id}/weather/refresh`, optionally scoped by `updateRequestId`.
Cache identity includes plan, request and threshold policy. Nearby nodes share raw
forecasts while retaining individual evidence. Missing values remain null; source
failures produce Partial/Unavailable/Stale states. ETA stays unknown with a reason
when speed/dwell data cannot support it.

## Before flight

While API is running, `MissionPreflightMonitor` polls once per minute. Approved missions
enter checking 15 minutes before StartTime and are rechecked every minute until they
start/end. Late API startup catches eligible flights within their unended window.
Multiple workers are bounded to four concurrent checks. Serializable transactions
and a recent-run guard avoid duplicate checks/alerts for the same mission.

Preflight results are stored separately from approval. `flightReadiness` is `Fail`,
`Pass`, `Incomplete`, `Error` or `AwaitingPreflight`; `preflightValidation` retains its findings even after
another manual validation. `Mission.Status` stays `Approved` until actual internal
start/cancel: approval history is not erased by failed readiness. An Incident with type
`PreflightValidation` warns operators/managers; repeat findings update the open alert.

`POST /api/missions/{id}/start` is an authorized internal lifecycle transition. It
requires approval, the scheduled time window and fresh mandatory validation. Failure
stores Blocked and returns 400. Passing checks mark InProgress. This endpoint sends
**no flight-control command** and does not physically prevent an external autopilot
from taking off. Telemetry ingestion is not discarded when flight readiness fails.

## Deployment and verification

The workflow SQL migrations add validation runs, update requests, weather cache,
restriction revisions/vertical metadata and approval audit links, with private RLS
and no anon/authenticated grants on the new evidence tables. Existing pending
missions are backfilled with initial requests; they are not automatically approved. Apply to each environment before starting this API version:

```sh
dotnet run --project be/DroneOps/DroneOps.API --no-launch-profile -- --migrate-mission-workflow --environment Development
```

The API checks that the migration exists before starting the monitor. Validation
rows contain explicit Pass/Fail/Incomplete/Error status, plan snapshots/hashes,
restriction revision, map/text evidence, actors, stages, timestamps and result JSON.
Historical attempts deliberately survive independently of mission existence. Use a
retention policy for these audit rows and hourly preflight volume.

```sh
dotnet build be/DroneOps/DroneOps.AuthChecks
dotnet run --project be/DroneOps/DroneOps.AuthChecks -- --mission-weather
dotnet run --project be/DroneOps/DroneOps.AuthChecks -- --mission-approval /absolute/path/to/SEP490
```

The PostgreSQL workflow test creates temporary user/pilot/UAV/geofence/mission data
and removes it in finally. It uses simulated weather; the weather parser test uses
HTTP fixtures. Neither claims hardware or live-weather verification.
