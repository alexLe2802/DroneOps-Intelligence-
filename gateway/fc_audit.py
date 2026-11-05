#!/usr/bin/env python3
"""Read-only FC commissioning evidence. Never writes parameters or flight commands."""
import argparse
import json
import math
import os
import time

os.environ.setdefault('MAVLINK20', '1')

ARDUPILOT_PARAMETERS = (
    'FRAME_CLASS', 'FRAME_TYPE', 'ARMING_CHECK', 'ARMING_OPTIONS',
    'WP_SPD', 'WP_SPD_UP', 'WP_SPD_DN', 'WP_RADIUS_M', 'WP_ACC',
    'WPNAV_SPEED', 'WPNAV_SPEED_UP', 'WPNAV_SPEED_DN', 'WPNAV_RADIUS', 'WPNAV_ACCEL',
    'PILOT_TKOFF_ALT', 'PILOT_SPEED_UP', 'LAND_SPEED', 'LAND_SPEED_HIGH', 'LAND_SPD_MS', 'LAND_SPD_HIGH_MS',
    'RTL_ALT', 'RTL_SPEED', 'RTL_ALT_FINAL', 'RTL_ALT_TYPE', 'RTL_ALT_M', 'RTL_SPEED_MS', 'RTL_ALT_FINAL_M', 'RTL_CLIMB_MIN_M',
    'FENCE_ENABLE', 'FENCE_TYPE', 'FENCE_ACTION', 'FENCE_ALT_MAX', 'FENCE_MARGIN',
    'FS_GCS_ENABLE', 'FS_GCS_TIMEOUT', 'FS_OPTIONS', 'FS_THR_ENABLE', 'FS_EKF_ACTION',
    'BATT_MONITOR', 'BATT_CAPACITY', 'BATT_LOW_VOLT', 'BATT_CRT_VOLT',
    'BATT_LOW_MAH', 'BATT_CRT_MAH', 'BATT_FS_LOW_ACT', 'BATT_FS_CRT_ACT', 'BATT_LOW_TIMER',
)
PX4_PARAMETERS = (
    'MPC_XY_CRUISE', 'MPC_XY_VEL_MAX', 'MPC_Z_V_AUTO_UP', 'MPC_Z_V_AUTO_DN',
    'MPC_TKO_SPEED', 'MPC_LAND_SPEED', 'NAV_ACC_RAD', 'GF_MAX_VER_DIST',
    'GF_ACTION', 'GF_MAX_HOR_DIST', 'RTL_RETURN_ALT', 'RTL_TYPE',
    'COM_DL_LOSS_T', 'NAV_DLL_ACT', 'COM_RC_LOSS_T', 'NAV_RCL_ACT',
    'COM_FAIL_ACT_T', 'COM_LOW_BAT_ACT', 'BAT_LOW_THR', 'BAT_CRIT_THR', 'BAT_EMERGEN_THR',
    'BAT1_CAPACITY', 'BAT1_N_CELLS', 'BAT1_V_CHARGED', 'BAT1_V_EMPTY',
)


def audit(link, duration=30, clock=time.monotonic, sleep=time.sleep):
    started = clock()
    target = None
    pending = []
    requested = set()
    report = {'schemaVersion': 1, 'readOnly': True, 'flightAuthorized': False,
              'firmware': None, 'parameters': {}, 'messages': {}, 'missingParameters': []}
    last_request = float('-inf')
    while clock() - started < duration:
        message = link.recv_match(blocking=False)
        if message is not None:
            kind = message.get_type()
            source = (message.get_srcSystem(), message.get_srcComponent())
            if target is None and kind == 'HEARTBEAT' and message.autopilot in (3, 12) and source[1] == 1:
                target = source
                report['systemId'], report['componentId'] = source
                report['firmware'] = 'ArduPilot' if message.autopilot == 3 else 'PX4'
                pending = list(ARDUPILOT_PARAMETERS if message.autopilot == 3 else PX4_PARAMETERS)
                # MAV_CMD_REQUEST_MESSAGE is read-only. Never send GCS heartbeat,
                # PARAM_SET, mode changes, mission uploads or actuator commands.
                for mid in (148, 242, 245, 1, 24, 147):
                    link.mav.command_long_send(*target, 512, 0, mid, 0, 0, 0, 0, 0, 0)
            if target is not None and source == target:
                if kind == 'PARAM_VALUE':
                    name = message.param_id
                    if isinstance(name, bytes):
                        name = name.decode('ascii', errors='ignore')
                    name = name.split('\0')[0]
                    if name in requested and math.isfinite(message.param_value):
                        report['parameters'][name] = message.param_value
                elif kind in ('HEARTBEAT', 'AUTOPILOT_VERSION', 'SYS_STATUS', 'GPS_RAW_INT',
                              'EXTENDED_SYS_STATE', 'BATTERY_STATUS', 'HOME_POSITION'):
                    data = message.to_dict()
                    # UID and exact location are not needed for the audit artifact.
                    for key in ('uid', 'uid2', 'lat', 'lon', 'latitude', 'longitude'):
                        data.pop(key, None)
                    report['messages'][kind] = data
                    if kind == 'AUTOPILOT_VERSION':
                        v = message.flight_sw_version
                        report['firmwareVersion'] = '%d.%d.%d' % (v >> 24, (v >> 16) & 255, (v >> 8) & 255)
        now = clock()
        if target and pending and now - last_request >= .1:
            name = pending.pop(0)
            requested.add(name)
            link.mav.param_request_read_send(*target, name.encode('ascii'), -1)
            last_request = now
        if message is None:
            sleep(.01)
    report['missingParameters'] = sorted(requested - report['parameters'].keys())
    report['heartbeatReceived'] = target is not None
    if report['firmware'] == 'ArduPilot':
        from arducopter_recovery import assess
        report['recoveryPreflight'] = assess(report['parameters'], report['messages'])
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--device', required=True, help='Dedicated router UDP endpoint or unowned serial port')
    parser.add_argument('--baud', type=int, default=115200)
    parser.add_argument('--duration', type=int, default=30, choices=range(10, 121))
    args = parser.parse_args()
    from pymavlink import mavutil
    link = mavutil.mavlink_connection(args.device, baud=args.baud, source_system=254, source_component=190)
    try:
        report = audit(link, args.duration)
        print(json.dumps(report, indent=2, allow_nan=False))
        return 0 if report['heartbeatReceived'] else 1
    finally:
        link.close()


if __name__ == '__main__':
    raise SystemExit(main())
