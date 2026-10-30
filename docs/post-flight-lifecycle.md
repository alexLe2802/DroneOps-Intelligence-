# Post Flight Lifecycle Contract

## Trusted completion

An armed-to-disarmed telemetry transition, disconnect, or disarm message is evidence only and never completes a mission. The configured gateway completes a flight by calling `POST /api/integrations/flight-completions` with integration credentials, an `Idempotency-Key`, and a supported event type: `MISSION_COMPLETED`, `FLIGHT_COMPLETED`, or `LANDED`. The mission must already be in a started/executing state; an `Approved` mission cannot be completed directly.

The accepted event is immutable and records mission, UAV, source identity, completion time, receipt time, the locked execution snapshot, and optional supplied evidence. The mission update, trusted event, and initial report are committed in one serializable transaction. PostgreSQL advisory locking, a unique idempotency key, and one trusted-completion-per-mission constraint protect retries and concurrent claims across API instances. Reusing an idempotency key with different content or sending a different completion for an already completed mission is rejected.

## Report lifecycle

Accepting a trusted completion changes the mission to `Completed` and creates report revision 1 in `Queued`. The background worker advances the current report through:

- `Queued`: waiting for a worker.
- `Generating`: generation is in progress. Work abandoned for more than five minutes is recoverable.
- `Available`: the report covers stored valid telemetry from mission start through the trusted completion time; both boundaries are stored on the revision.
- `Failed`: generation failed; flight, telemetry, incidents, and the failure reason remain available.
- `Outdated`: valid telemetry for the covered flight window arrived after generation.

Only an Operations Manager can call `POST /api/flight-history/{missionId}/regenerate`. A reason is required. Regeneration creates a new current revision and preserves every older revision. Revision history is available from `GET /api/flight-history/{missionId}/report/revisions`.

## Analytics formula and coverage

The exact role-scoped formulas, units, coverage exclusions, and source links for `GET /api/flight-history/analytics` are documented in `docs/operational-analytics-contract.md`.

## Deployment

Apply migrations in `database/migration-order.txt`, including `007_post_flight_lifecycle.sql` and `20261029000100_post_flight_execution_snapshot.sql`, or run the API once with `--migrate-postflight`, before starting the upgraded API normally. The migrations preserve existing reports as revision 1, add immutable coverage-window starts, replace the legacy one-report-per-mission constraint with report revisions plus one current revision, and enforce one trusted completion per mission.

The real PostgreSQL lifecycle fixture can be run with:

```powershell
dotnet run --project be/DroneOps/DroneOps.PostFlightChecks -- --lifecycle-postgres <absolute-repository-path>
```

It covers concurrent claims, same-key replay, ambiguous completion, pre-start rejection, authorization, late-data invalidation, revision preservation, and generation failure evidence.
