#!/usr/bin/env python3
"""Isolated ArduCopter 4.7.1 SITL parameter application/read-back check.

Starts its own simulator process and connects only to its localhost TCP port.
Never accepts a real FC endpoint. This is not an in-flight failsafe test.
"""
import argparse
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time

from pymavlink import mavutil
from arducopter47_adapter import apply_to_sitl, parameter_targets


INSTANCE = 8
PORT = 5760 + 10 * INSTANCE
BATTERY_PARAMETERS = {
    'BATT_MONITOR': 4, 'BATT_CAPACITY': 8000,
    'BATT_LOW_VOLT': 14.0, 'BATT_CRT_VOLT': 13.2,
    'BATT_LOW_MAH': 0, 'BATT_CRT_MAH': 0,
    'BATT_FS_LOW_ACT': 2, 'BATT_FS_CRT_ACT': 1,
}
ENVELOPE = {
    'fcParameterVerificationRequired': True,
    'cruiseSpeedMetersPerSecond': 3,
    'maxSpeedMetersPerSecond': 5,
    'limits': {
        'policyVersion': 'multicopter-envelope-v1',
        'takeoffSpeedMetersPerSecond': 1,
        'climbSpeedMetersPerSecond': 1,
        'descentSpeedMetersPerSecond': .7,
        'landingSpeedMetersPerSecond': .7,
        'maxAltitudeMeters': 30,
        'recoveryEnergyProfile': {'returnAltitudeMeters': 20},
    },
}


def read_parameter(link, name, timeout=5):
    link.mav.param_request_read_send(1, 1, name.encode('ascii'), -1)
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        packet = link.recv_match(type='PARAM_VALUE', blocking=True, timeout=.5)
        if packet is None or packet.get_srcSystem() != 1 or packet.get_srcComponent() != 1:
            continue
        actual = packet.param_id.decode('ascii', 'ignore') if isinstance(packet.param_id, bytes) else packet.param_id
        if actual.split('\0')[0] == name:
            return packet.param_value
    raise RuntimeError(f'SITL did not return {name}')


def run(binary):
    binary = Path(binary).resolve(strict=True)
    with tempfile.TemporaryDirectory(prefix='droneops-sitl-47-') as work:
        with open(Path(work) / 'sitl.log', 'wb') as log:
            process = subprocess.Popen([str(binary), '-w', '-S', '-I', str(INSTANCE),
                '--home', '-35.363261,149.165230,584,353', '--model', '+',
                '--speedup', '1'], cwd=work, stdout=log, stderr=subprocess.STDOUT)
            link = None
            try:
                deadline = time.monotonic() + 40
                while time.monotonic() < deadline:
                    if process.poll() is not None:
                        raise RuntimeError('ArduCopter SITL exited before MAVLink became available.')
                    try:
                        link = mavutil.mavlink_connection(f'tcp:127.0.0.1:{PORT}', source_system=250, source_component=190)
                        hb = link.wait_heartbeat(timeout=3)
                        if hb and hb.autopilot == 3 and hb.type == 2:
                            break
                        link.close(); link = None
                    except (OSError, ConnectionError):
                        time.sleep(.5)
                else:
                    raise RuntimeError('SITL heartbeat timeout.')
                link.mav.command_long_send(1, 1, 512, 0, 148, 0, 0, 0, 0, 0, 0)
                version = link.recv_match(type='AUTOPILOT_VERSION', blocking=True, timeout=8)
                if version is None or version.get_srcSystem() != 1 or version.get_srcComponent() != 1:
                    raise RuntimeError('SITL did not identify its firmware.')
                number = version.flight_sw_version
                firmware = f'{number >> 24}.{(number >> 16) & 255}.{(number >> 8) & 255}'
                if firmware != '4.7.1':
                    raise RuntimeError(f'Expected ArduCopter 4.7.1, received {firmware}.')
                # These voltage values are for a simulated 4S pack only. They
                # must never be copied to a physical battery configuration.
                apply_to_sitl(link, BATTERY_PARAMETERS)
                current = {name: read_parameter(link, name) for name in ('FENCE_TYPE', 'FS_OPTIONS', *BATTERY_PARAMETERS)}
                targets = parameter_targets(ENVELOPE, current)
                apply_to_sitl(link, targets)
                readback = {name: read_parameter(link, name) for name in targets}
                for name, wanted in targets.items():
                    if abs(readback[name] - wanted) > 1e-4:
                        raise RuntimeError(f'SITL read-back mismatch: {name}')
                print(json.dumps({'simulator': 'ArduCopter', 'version': firmware,
                    'parameterCount': len(targets), 'independentReadback': True,
                    'flightAuthorized': False, 'flightFaultScenariosTested': False}))
            finally:
                if link is not None:
                    link.close()
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill(); process.wait(timeout=5)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--binary', required=True, help='Official ArduCopter 4.7.1 Linux SITL binary')
    run(parser.parse_args().binary)
