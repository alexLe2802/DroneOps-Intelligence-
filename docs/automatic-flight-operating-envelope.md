# Automatic mission authoring, FC defaults and recovery policy

Status: implementation on Qthong; physical execution remains disabled. This is
not a claim of Mission Planner/QGroundControl feature parity or successful SITL.

## Authoring modes and required data

The existing `automaticFlightRequested` flag selects the mode; no duplicate
mission API is introduced. Manual mode retains the application's planned route,
UAV and schedule review, but does not upload/arm/start an automatic flight.

| Field | Required at creation? | Source / handling |
| --- | --- | --- |
| Mission name | Yes in DroneOps | Registry/review identifier; this app requirement differs from a local GCS plan file |
| Assigned UAV and start/end time | Yes | Existing owner/active-UAV/schedule validation |
| Latitude, longitude, altitude, command, order | Yes for route nodes | Existing route approval; first TakeOff and final Land in the supported multicopter profile |
| Altitude reference | Yes for automatic mode | Relative to takeoff Home; not a terrain-clearance or legal-altitude assertion |
| Home | Not typed at creation; required before flight | Bound FC HOME_POSITION, fresh navigation evidence; never infer actual Home from first map click |
| Cruise and maximum horizontal speed | Optional input in automatic UI | Planning defaults 3 m/s; user override <= 5 m/s; FC defaults can lower compiled cruise but must not raise it beyond the approved envelope |
| Takeoff/climb/descent/landing limits | Optional override | Prefilled system policy; user does not need manufacturer performance figures |
| Waypoint speed | Optional | Bound FC/mission default, with an explicit override <= approved mission maximum |
| Hold time | Optional, required for Hover | Blank FlyThrough means no hold. There is no universal FC default hold time; Hover must have a positive duration |
| Acceptance radius | Optional | Explicit value or observed FC parameter, with supported range >0 to 10 m. Unknown is not guessed |
| Pass radius | Optional but unavailable in multicopter v1 | Nonzero value rejected, not silently ignored |
| Yaw | Optional | Unset uses FC yaw behavior; firmware support must be checked for explicit angles |
| Landing coordinates | Required for Land endpoint | Existing route endpoint; ground altitude required before compiler/flight, not assumed from cruising altitude |
| Emergency landing candidate | Optional at creation | Needs ground altitude and clearance notes; manager review is not a substitute for actual landing/approach verification |
| RTL ending / arbitrary MAV_CMD / camera / gimbal | Not enabled in this phase | UI does not offer unsupported commands. Current route ends with Land; recovery-to-Home is a separate decision contract |

Waypoint order is generated from the route and can be rearranged; users do not
need to type sequence numbers. Required/optional labels are shown on the form.
Optional override clearing returns to the stated policy/default source.

## Trial multicopter operating envelope

These are initial product policy choices for validation and controlled testing,
not official universal safe values from Mission Planner, QGC or either firmware.

| Setting | Initial value | User-selectable range/cap |
| --- | --- | --- |
| Cruise | 3 m/s = 10.8 km/h | >=0.5 m/s, <= selected maximum |
| Horizontal speed ceiling | 3 m/s initially | <=5 m/s = 18 km/h |
| New waypoint altitude | 20 m | Must fit selected ceiling and route/airspace checks |
| Altitude ceiling | 30 m above takeoff Home | >10 m to 30 m |
| Takeoff | 1 m/s | 1–1.5 m/s |
| Climb | 1 m/s | >= takeoff and <=1.5 m/s |
| Descent | 0.7 m/s | 0.6–1 m/s |
| Landing | 0.7 m/s | 0.6–0.7 m/s and <= descent |
| Forecast sustained wind | 3 m/s | User may lower; never exceed 3 m/s |
| Forecast gust | 5 m/s | >= wind threshold and <=5 m/s |

The <=10 km/h preset uses 2.77 m/s (9.972 km/h), avoiding upward unit rounding.
This limits the target; it cannot guarantee actual groundspeed never exceeds
10 km/h in wind, a transient or a control fault. Return/landing feasibility must
also account for braking, gusts, estimator error, obstacles and remaining energy.
Slower motion can consume more total energy because the flight takes longer.

For automatic missions, current validation blocks out-of-envelope values and
requires fresh wind/gust forecast. It does not call a missing forecast safe.
Battery estimation reuses the existing calculator and conservatively includes
vertical travel, takeoff, landing and supplied holds. It remains an uncalibrated
endurance estimate and cannot authorize an emergency return leg.

## FC/GCS comparison and parameter sources

Mission Planner and QGC author/upload plans; the flight controller executes
them. Omitted mission speed is not a universal 5 or 10 m/s chosen by the website.
Firmware parameters, mission speed commands and airframe/controller constraints
affect the executed speed. A speed target does not automatically create an
overspeed failsafe in every firmware.

| Purpose | ArduCopter 4.6.x examples | PX4 1.17 examples |
| --- | --- | --- |
| Mission horizontal speed | WPNAV_SPEED (cm/s), DO_CHANGE_SPEED (m/s) | MPC_XY_CRUISE, mission speed override |
| Controller horizontal constraint | Requires validated mode/command-specific adapter | MPC_XY_VEL_MAX affects all velocity-controlled modes, not only AUTO |
| Climb/descent | WPNAV_SPEED_UP / WPNAV_SPEED_DN (cm/s) | MPC_Z_V_AUTO_UP / MPC_Z_V_AUTO_DN and global vertical constraints |
| Takeoff | Must verify actual AUTO takeoff rate implementation | MPC_TKO_SPEED |
| Landing | LAND_SPEED, LAND_SPEED_HIGH and approach configuration | MPC_LAND_SPEED, landing stage configuration |
| Altitude fence | FENCE_ALT_MAX plus enable/type/action/margin | GF_MAX_VER_DIST plus action configuration |
| Return | RTL_SPEED, RTL_ALT, rally behavior | RTL_RETURN_ALT, RTL_TYPE and return configuration |

ArduPilot parameter names/units change across versions (for example newer
WP_SPD-family parameters). Read actual firmware identity and metadata; do not
blindly multiply every speed by 100. PX4's MPC_XY_CRUISE configuration metadata
has a 3 m/s lower bound in the referenced release; lower mission speed overrides
must be verified in the adapter, not assumed to be accepted as a global parameter.
PX4 takeoff minimum is 1 m/s and landing minimum is 0.6 m/s in that release.

Gateway now requests supported navigation parameters read-only with
PARAM_REQUEST_READ, filters source system/component, records firmware version,
timestamp and bound hardware identity, and exposes a planning cache through
the existing UAV response. No new PARAM_SET writes are made. Cached data is not
an ARM receipt. The compiler can resolve radius/cruise from the observed values;
an explicit override remains bound to the plan/compiled hash.

Changing operational limits or waypoint navigation changes the automatic plan
hash and requires fresh review/confirmation. Existing automatic missions without
an explicit envelope must be resubmitted; migration does not invent an approval.

## Recovery arbitration and simultaneous faults

Recovery must begin before depletion. Zero percent, a power failure or a critical
voltage indication cannot guarantee that energy remains for any flight maneuver.

The new deterministic decision contract considers all simultaneous faults:

1. Confirmed ground state inhibits launch; no automatic ARM/force-disarm.
2. FC critical-battery signal or <=15% takes priority over RTL/control loss and
   overspeed. Request the verified native emergency procedure; do not start a
   new horizontal return leg.
3. Invalid/stale navigation or unknown battery does not authorize an invented
   route. The preconfigured native FC fallback must remain available locally.
4. At <=30%, control loss, or verified inability to reach Home with reserve,
   prefer Home only when its landing approach and energy-to-land are verified.
5. Otherwise choose the nearest approved, landing-verified, approach-clear
   alternate that is energy-reachable **including landing reserve**. Ordinary
   mission waypoints are excluded.
6. With no verified reachable option, use the approved native emergency
   procedure; never blindly navigate to the closest coordinate.
7. Reevaluate an active return as energy falls. A committed recovery landing
   is not interrupted by a new control-loss RTL. Restored communications never
   auto-resume the original mission.

The energy decision requires a calibrated model and current available energy,
return/approach/landing cost and positive landing reserve. Percent * capacity or
distance alone is not proof. A route/site must be both approved and physically
verified. The decision object explicitly returns `FcExecutionVerified=false`.

Initial overspeed detection proposal: horizontal > selected maximum +
max(0.5 m/s, 20%), vertical rate > applicable phase limit +0.3 m/s, or altitude
> ceiling +2 m, sustained for 2 seconds. These tolerances are **not permission
to command higher speeds/heights**. They avoid acting on one noisy packet and
need airframe/firmware validation. Sustained breach yields HoldAndReassess;
battery/navigation/control-loss priority can choose a different recovery action.
The engine distinguishes takeoff/climb and landing/descent rates.

ArduPilot/PX4 native battery, RC, GCS/data-link, navigation and fence protections
must be configured and read back before ARM. Native rally/RTL behavior is not
identical to this application's Home-first energy policy. An adapter must prove
the intended destination and action; merely sending RTL is insufficient.

## Implemented boundaries and remaining release work

Implemented: form separation and field labeling; optional waypoint metadata;
manager comparison/hash binding; AI context and mandatory policy checks;
read-only FC parameter capture/cache; compiler mapping of supported overrides;
deterministic failsafe arbitration and combined-fault tests.

Not implemented/verified: versioned FC parameter application/read-back for the
operational envelope; onboard configuration of the recovery contract; native
rally/fence upload and exact recovery-route execution; calibrated emergency
energy model; physical landing/obstacle/terrain verification; ArduPilot/PX4 SITL
and hardware flight tests. Local environment currently has no Docker/CMake/Ninja
SITL toolchain. This is not complete GCS feature parity.

The compiled envelope declares `fcParameterVerificationRequired=true`. Gateway
explicitly refuses to upload/start an envelope without its versioned parameter
verification adapter. Backend execution flag remains off; no production/Jetson
flight enablement is performed. Unit/protocol tests are not SITL evidence.
Backend additionally requires a matching `verifiedEnvelopeHash` receipt before
issuing upload/ARM/start, so an older gateway that ignores the new envelope
cannot bypass the gate. Current gateway does not produce such a receipt; only
the isolated fixture simulates it to exercise subsequent transitions.

## API/data changes

No endpoint family was duplicated. Existing mission request/response/proposal
JSON adds `automaticFlightLimits`; waypoint objects add `navigation`. Existing
UAV create/update/read adds `batteryMaxVoltage`; read adds
`firmwareNavigationDefaults`. The gateway `controlPoll` evidence adds
`firmwareParameters` and fresh FC Home coordinates; Live Monitoring displays
that Home reference. Existing automation plan adds limits/recovery policy.
AI responses/context include limits, waypoint options and recovery contract.

Existing `batteryVoltage` is kept as legacy nominal voltage; it is not renamed
or copied into max voltage. Battery Wh is never computed from the max-voltage
field. Old max-speed/wind/operating-altitude data is retained for historical
compatibility but removed from the registration form.

Migration: `20261104163328_automatic_flight_envelope_and_battery_max_voltage.sql`.
Nullable JSON/text and voltage columns preserve legacy data. Back up database
and private Storage before application; do not enable aircraft execution.

## Primary references

- [QGC plan settings](https://docs.qgroundcontrol.com/Stable_V5.1/en/qgc-user-guide/plan_view/plan_view.html)
- [ArduCopter 4.6.3 parameters](https://ardupilot.org/copter/docs/parameters-Copter-stable-V4.6.3.html)
- [PX4 1.17 parameters](https://docs.px4.io/v1.17/en/advanced_config/parameter_reference)
- [ArduPilot battery failsafe](https://ardupilot.org/copter/docs/failsafe-battery.html)
- [ArduPilot rally points](https://ardupilot.org/copter/docs/common-rally-points.html)
- [PX4 failsafe configuration](https://docs.px4.io/main/en/config/safety)
