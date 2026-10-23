# Post Flight Lifecycle Contract

## Trusted completion

An armed-to-disarmed telemetry transition is evidence only and never completes a mission. The configured gateway completes a flight by calling `POST /api/integrations/flight-completions` with integration credentials, an `Idempotency-Key`, and a supported event type: `MISSION_COMPLETED`, `FLIGHT_COMPLETED`, or `LANDED`.

The accepted event is immutable and records mission, UAV, source identity, completion time, receipt time, and optional evidence. Reusing an idempotency key with different content is rejected.

## Report lifecycle

Accepting a trusted completion changes the mission to `Completed` and creates report revision 1 in `Queued`. The background worker advances the current report through:

- `Queued`: waiting for a worker.
- `Generating`: generation is in progress. Work abandoned for more than five minutes is recoverable.
- `Available`: the report covers stored valid telemetry through the trusted completion time.
- `Failed`: generation failed; flight, telemetry, incidents, and the failure reason remain available.
- `Outdated`: valid telemetry for the covered flight window arrived after generation.

Only an Operations Manager can call `POST /api/flight-history/{missionId}/regenerate`. A reason is required. Regeneration creates a new current revision and preserves every older revision. Revision history is available from `GET /api/flight-history/{missionId}/report/revisions`.

## Analytics formula and coverage

`GET /api/flight-history/analytics` accepts `fromDate`, `toDate`, `uavId`, and `missionId`. The completion-rate denominator is every authorized terminal mission (`Completed`, `Failed`, `Aborted`, or `Cancelled`) in the selected window. The numerator is terminal missions with status `Completed`. If the denominator is zero, the rate is `null`, never 100 percent.

Distance and duration totals use only current `Available` reports. The response exposes available/excluded report counts, coverage percentage, exclusion reasons, daily completion trend, and incident severity breakdown. This prevents missing or failed reports from being presented as zero risk.

## Deployment

Apply `database/migrations/007_post_flight_lifecycle.sql`, or run the API once with `--migrate-postflight`, before starting the upgraded API normally. The migration preserves existing reports as revision 1 with status `Available` and replaces the legacy one-report-per-mission constraint with immutable report revisions plus one current revision.
