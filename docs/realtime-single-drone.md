# Realtime monitoring — one drone

This release uses one registered aircraft. Detector/timer/operator mutations share
one PostgreSQL transaction advisory lock, including across API instances. Measured
recovery (`Incident.RecoveredAt`) is independent of manual resolution. Only two
consecutive fresh samples from the same source/UAV can end a numeric-rule episode.
Closing an incident does not start a new episode. Operator may acknowledge;
Operations Manager may resolve. Web mutations must include `expectedStatus` from
the record the user reviewed; an outdated state returns HTTP 409.

Apply `20261110000100_realtime_episodes.sql` after the existing telemetry migrations,
or use the API's existing `--migrate-telemetry` command. This adds nullable fields
without discarding evidence. Apply 20261110000200_realtime_execution.sql next. Neither migration has been applied to a deployed DB by this change.

The enrolled gateway remains the normal aircraft connection. Legacy HTTP ingestion
requires an explicit source/UAV allowlist; an empty list rejects all legacy sources:

```json
{
  "IntegrationSettings": {
    "Sources": [{ "SourceId": "your-source", "UavId": "your-registered-uav-uuid" }]
  },
  "TelemetryMonitoring": {
    "CommunicationLossSeconds": 10,
    "ScanIntervalSeconds": 1,
    "MinimumAltitudeMslMeters": null,
    "MaximumAltitudeMslMeters": null
  }
}
```

Set both MSL limits to the actual operating area's permitted elevations. No terrain
or home elevation is invented. Gateway forwards GLOBAL_POSITION_INT MSL altitude
separately from relative altitude. Without MSL data/limits, altitude detection is
Unavailable with a visible diagnostic. Speed uses UAV.MaxSpeedKmh converted to m/s;
missing aircraft maximum disables that rule with a diagnostic. Battery below 20%
for two samples raises Warning; two samples at or above 25% recover. Altitude and
speed violations are Critical and recover after two within-limit samples.

MISSION_CURRENT identifies the current autopilot command. It is not a reached
waypoint count and may include home/takeoff/landing commands. Progress now uses confirmed reached commands mapped by the compiler to the immutable execution snapshot; before observed execution it stays null / Unavailable.
The UI does not synthesize a percentage from the command index.

The targeted checks use one primary UAV; a second dummy ID remains only to check
rejection of an incorrect aircraft. Load, soak, browser acceptance and hardware
acceptance tasks are outside this implementation pass; the release performance
requirements have not been weakened.

Latest implementation and the single pending acceptance case: [realtime-mainflow-completion.vi.md](realtime-mainflow-completion.vi.md). No tests executed in the completion pass.
