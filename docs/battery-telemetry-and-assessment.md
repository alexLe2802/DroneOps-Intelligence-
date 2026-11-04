# Battery telemetry and mission assessment

## What the gateway currently reads

| Existing field | Meaning | Source |
| --- | --- | --- |
| batteryPercent | Remaining percentage, or null if unknown | BATTERY_STATUS / SYS_STATUS |
| batteryConsumedMah | Charge already consumed, not full pack capacity | BATTERY_STATUS.current_consumed |
| batteryVoltageVolts | Pack voltage | BATTERY_STATUS / SYS_STATUS |
| batteryCurrentAmps | Battery current | BATTERY_STATUS / SYS_STATUS |
| batteryTemperatureCelsius | Reported temperature | BATTERY_STATUS |
| batteryCellVoltagesVolts | Individual cell readings when supplied | BATTERY_STATUS |
| UAV.batteryCapacityMah | Configured installed pack capacity | Existing UAV specification form/API |

The gateway uses primary battery ID 0, checks FC system/component IDs, and expires
individual readings after five seconds. Unknown sentinels become null; zero is a
valid measurement. The backend preserves the existing telemetry fields.
Capacity is not currently read from the FC. SMART_BATTERY_INFO (370) can expose
full/design capacity on supporting systems. The newer BATTERY_INFO (372) is a
different message with a different capacity representation. This gateway requests
and decodes neither message. Availability depends on
firmware, battery monitor and battery hardware. A telemetry radio only transports
what the FC reports; it does not measure battery capacity itself.

Sources: [BATTERY_STATUS](https://mavlink.io/en/messages/common.html#BATTERY_STATUS),
[battery information](https://mavlink.io/en/messages/common.html#BATTERY_INFO).

## Assessment behavior

The existing POST/GET `/api/ai/assessments` endpoints now expose `result.battery`.
No new endpoint or database migration is required. The server loads authorized
mission waypoints, the matching UAV specification, telemetry and matching recent
weather evidence. The client sends only missionRef and missionVersion; client
claims about battery measurements or capacity are not trusted.

Numeric estimates require all of the following:

- Valid telemetry for this mission and UAV, with source and receive timestamps
  no older than 15 seconds (future skew over five seconds is rejected).
- Remaining battery percentage in [0, 100].
- Positive installed pack capacity configured on the UAV.
- A usable planned route, average speed and configured maximum flight duration.

The assessment selects the newest sample, even when its battery value is unknown;
it does not silently replace it with an older plausible percentage. Missing data
produces `status=MissingData`, null requirement/sufficiency values and explicit
missingData entries. Consumed mAh is optional evidence and never used to reverse
infer full capacity. Voltage is not converted into an invented percentage.

For available evidence, `status=EstimateOnly` reuses MissionBatteryRiskCalculator:

- Route distance / average speed gives estimated route duration.
- Route duration / configured full-charge endurance gives base consumption %.
- The existing weather heuristic and 20% capacity reserve are applied.
- Required mAh estimate = configured capacity × required percentage / 100.
- Remaining mAh estimate = configured capacity × reported percentage / 100.

`meetsEstimatedRequirement` compares the fresh percentage with this estimate;
it is not a preflight pass, approval, ARM or launch permission. Instantaneous
current while idle is not extrapolated as in-flight consumption. Hover, climb,
payload, pack aging and the match between configured/installed pack need separate
calibration; forecasts are only reused when recent, unexpired and tied to the
current approved-plan hash. Without a forecast, the missing adjustment is stated.
The existing historical-evidence provider stores battery inputs/results in its
context snapshot; these calculations are deterministic, not LLM-generated values.

Example: a configured 5200 mAh pack reporting 70% gives **3640 mAh estimated
remaining**. A telemetry reading of 300 mAh consumed is kept separately; it is
neither 300 mAh capacity nor evidence of a fully charged start.

The AI page can select planned missions through the existing mission APIs,
instead of requiring a completed flight and recorded coordinates. Assessment
history is a stored snapshot; generate a new assessment to refresh battery data.
Live Monitoring shows percent, voltage, current, consumed mAh and separately labeled
configured capacity. Existing telemetry and UAV API contracts remain unchanged.
Battery state, capacity and readiness are backend context, not extra navigation
fields to insert into `.plan` mission items.

## Camera deferred

New/edit mission forms offer FlyThrough and Hover for intermediate waypoints.
The existing create/update validator rejects CapturePhoto with a deferred-feature
message. Existing CapturePhoto records remain readable and are not rewritten or
deleted; editing such a waypoint requires an explicit replacement action.
No camera measurements or camera fields are used in battery assessment.
