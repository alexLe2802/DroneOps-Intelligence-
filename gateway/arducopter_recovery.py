"""Read-only ArduCopter recovery preflight checks.

This is diagnostic evidence, never an authorization receipt or a PARAM_SET plan.
Mission Planner exposes these settings through its Failsafe and Full Parameter
screens; their presence alone does not establish a safe return route.
"""
import math


REQUIRED = (
    'ARMING_CHECK', 'FS_THR_ENABLE', 'FS_GCS_ENABLE', 'FS_GCS_TIMEOUT',
    'FS_OPTIONS', 'FS_EKF_ACTION', 'FENCE_ENABLE', 'FENCE_TYPE',
    'FENCE_ACTION', 'FENCE_ALT_MAX', 'BATT_MONITOR', 'BATT_CAPACITY',
    'BATT_LOW_VOLT', 'BATT_CRT_VOLT', 'BATT_LOW_MAH', 'BATT_CRT_MAH',
    'BATT_FS_LOW_ACT', 'BATT_FS_CRT_ACT', 'RTL_ALT_M', 'RTL_SPEED_MS',
    'RTL_ALT_FINAL_M', 'WP_SPD', 'WP_SPD_UP', 'WP_SPD_DN', 'LAND_SPD_MS',
)


def assess(parameters, messages):
    """Return explicit blockers from FC observations; always require field trials."""
    values = {k: v for k, v in parameters.items()
              if isinstance(v, (int, float)) and math.isfinite(v)}
    blockers = [f'Missing FC parameter: {name}' for name in REQUIRED if name not in values and name != 'ARMING_CHECK']

    def check(name, predicate, reason):
        if name in values and not predicate(values[name]):
            blockers.append(reason)

    if 'ARMING_SKIPCHK' in values:
        check('ARMING_SKIPCHK', lambda x: x == 0, 'FC arming checks are skipped.')
    elif 'ARMING_CHECK' in values:
        check('ARMING_CHECK', lambda x: x == 1, 'FC arming checks are not all enabled.')
    else:
        blockers.append('Missing FC arming-check configuration (ARMING_SKIPCHK or legacy ARMING_CHECK).')
    check('FS_THR_ENABLE', lambda x: x != 0, 'RC-loss failsafe is disabled.')
    check('FS_GCS_ENABLE', lambda x: x in (1, 3, 4, 5, 6, 7),
          'GCS-loss failsafe is disabled or has an unreviewed action.')
    check('FS_GCS_TIMEOUT', lambda x: 1 <= x <= 120, 'GCS-loss timeout is invalid.')
    check('FS_EKF_ACTION', lambda x: x != 0, 'EKF-loss action is disabled.')
    check('FS_OPTIONS', lambda x: int(x) == x and x >= 0 and not (int(x) & 23),
          'FS_OPTIONS permits continuing AUTO, GUIDED or pilot control after RC/GCS loss; review recovery policy.')
    check('FENCE_ENABLE', lambda x: x == 1, 'FC geofence is disabled.')
    check('FENCE_TYPE', lambda x: int(x) == x and (int(x) & 1) != 0,
          'FC altitude fence is not selected.')
    check('FENCE_ACTION', lambda x: x != 0, 'Fence action only reports the breach.')
    check('FENCE_ALT_MAX', lambda x: x > 0, 'Fence altitude limit is invalid.')
    check('BATT_MONITOR', lambda x: x != 0, 'FC battery monitor is disabled.')
    check('BATT_CAPACITY', lambda x: x > 0, 'FC battery capacity is not configured.')
    check('BATT_FS_LOW_ACT', lambda x: x != 0, 'Low battery has no FC failsafe action.')
    check('BATT_FS_CRT_ACT', lambda x: x != 0, 'Critical battery has no FC failsafe action.')
    for name in ('BATT_LOW_VOLT', 'BATT_CRT_VOLT', 'BATT_LOW_MAH', 'BATT_CRT_MAH'):
        check(name, lambda x: x >= 0, f'{name} is invalid.')
    if all(name in values for name in ('BATT_LOW_VOLT', 'BATT_LOW_MAH')) and not (values['BATT_LOW_VOLT'] > 0 or values['BATT_LOW_MAH'] > 0):
        blockers.append('Low battery has no voltage or remaining-capacity threshold.')
    if all(name in values for name in ('BATT_CRT_VOLT', 'BATT_CRT_MAH')) and not (values['BATT_CRT_VOLT'] > 0 or values['BATT_CRT_MAH'] > 0):
        blockers.append('Critical battery has no voltage or remaining-capacity threshold.')
    if all(name in values for name in ('BATT_LOW_VOLT', 'BATT_CRT_VOLT')) and values['BATT_LOW_VOLT'] > 0 and values['BATT_CRT_VOLT'] > 0 and values['BATT_CRT_VOLT'] >= values['BATT_LOW_VOLT']:
        blockers.append('Critical voltage must be lower than low voltage.')

    battery = messages.get('BATTERY_STATUS') or messages.get('SYS_STATUS') or {}
    if battery:
        if 'voltages' in battery:
            observed = [v for v in battery['voltages'] if isinstance(v, (int, float)) and 0 < v < 65535]
            pack_volts = sum(observed) / 1000 if observed else None
        else:
            raw = battery.get('voltage_battery')
            pack_volts = raw / 1000 if isinstance(raw, (int, float)) and raw != 65535 else None
        if pack_volts is None or pack_volts < 1:
            blockers.append('Live battery voltage is absent or implausible; verify flight pack and power module.')
    else:
        blockers.append('Live FC battery telemetry is absent.')

    gps = messages.get('GPS_RAW_INT') or {}
    if gps.get('fix_type', 0) < 3:
        blockers.append('No 3D GPS fix from FC.')
    if 'HOME_POSITION' not in messages:
        blockers.append('No FC Home position.')

    return {'flightAuthorized': False, 'parameterAuditPassed': not blockers,
            'batteryCapacityEvidence': {
                'configuredCapacityMah': values.get('BATT_CAPACITY'),
                'source': 'FC parameter BATT_CAPACITY',
                'measuredCapacityMah': None,
                'capacityVerified': False,
                'remainingCapacityVerified': False,
            },
            'blockers': blockers,
            'remainingVerification': [
                'Check real battery chemistry/cell count, calibrated capacity, voltage and reserve.',
                'Survey Home, landing alternatives, terrain, obstacles and airspace.',
                'Apply and read back a versioned FC profile bound to the exact mission hash.',
                'Exercise RC/GCS/battery/navigation/fence and combined faults in ArduCopter SITL.',
                'Verify controlled hardware and supervised flight behavior before production release.',
            ]}
