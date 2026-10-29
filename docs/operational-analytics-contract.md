# KAN-74 — Operational analytics contract

Endpoint: `GET /api/flight-history/analytics`. Operators see only missions they
created; operations managers see the system-wide scope. `uavId` and `missionId`
filters are applied inside that role scope and can never expand it.

The date window is inclusive and uses `Mission.StartTime` in UTC. A started flight
has a non-null start time and status `In Progress`, `InProgress`, `Active`, `Flying`,
`Executing`, `Completed`, `Failed` or `Aborted`. Draft, approved-only and cancelled
before-flight records are not denominator entries.

`successRatePercentage = Completed / all started flights × 100`. When no started
flight matches, the value is `null` and `completionRateStatus` is `Unavailable`.

Recorded distance is calculated from chronological, valid telemetry—not planned
waypoints and not cached report totals. A segment requires two consecutive samples
with valid GPS. Missing GPS breaks the chain. A time gap strictly longer than
`TelemetryMonitoring:CommunicationLossSeconds` is excluded. The response reports
valid segments, excluded segments, excluded gap seconds, missing GPS samples and
invalid telemetry samples, plus missions without usable GPS coverage.

Incident totals and severity buckets count distinct `Incident.Id` values. Incident
transition/audit rows never increase incident totals.

The analytics GET is read-only. Scheduled lifecycle synchronization is performed by
the post-flight monitor/service and is not triggered as a side effect of reading the
dashboard.

`contract` declares UTC/window basis, units, communication-loss threshold and API
source links. `missingCoverage` and report exclusion fields make partial coverage
explicit; consumers must not display missing coverage as zero-distance certainty.

PostgreSQL verification:

```powershell
dotnet run --project be/DroneOps/DroneOps.PostFlightChecks -- `
  --analytics-postgres <absolute-repository-path>
```

The fixture creates owner/out-of-scope missions, UAV filters, a cancelled mission,
an outside-window mission, GPS gaps/missing fixes, distinct incidents and multiple
transitions. It removes all temporary records after the run.
