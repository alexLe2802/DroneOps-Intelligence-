#!/usr/bin/env python3
"""Interactive radio gateway launcher. No laptop or drone IP configuration."""
import argparse
import os
from pathlib import Path
import subprocess
import sys
import time

os.environ.setdefault('MAVLINK20', '1')


def choose_port(ports, ask=input):
    if not ports:
        raise RuntimeError('No serial device found. Plug the ground telemetry radio into USB and try again.')
    if len(ports) == 1:
        print('USB device:', ports[0].device)
        return ports[0].device
    for index, port in enumerate(ports, 1):
        print('%d. %s — %s' % (index, port.device, port.description))
    while True:
        selected = ask('Select the ground telemetry radio number: ').strip()
        if selected.isdigit() and 1 <= int(selected) <= len(ports):
            return ports[int(selected) - 1].device
        print('Choose a number from the list.')


def detect_autopilot(port, mavutil, bauds=(57600, 115200, 921600, 460800, 38400)):
    for baud in bauds:
        link = mavutil.mavlink_connection(port, baud=baud)
        try:
            deadline = time.monotonic() + 3
            while time.monotonic() < deadline:
                message = link.recv_match(type='HEARTBEAT', blocking=True, timeout=max(0, deadline - time.monotonic()))
                if message is None:
                    break
                if (message.autopilot in (mavutil.mavlink.MAV_AUTOPILOT_ARDUPILOTMEGA, mavutil.mavlink.MAV_AUTOPILOT_PX4)
                        and message.get_srcComponent() == mavutil.mavlink.MAV_COMP_ID_AUTOPILOT1):
                    from gateway import read_identity
                    identity = read_identity(link, message.get_srcSystem(), message.get_srcComponent(), mavutil)
                    return baud, message.get_srcSystem(), message.get_srcComponent(), identity
        finally:
            link.close()
    raise RuntimeError('No ArduPilot/PX4 autopilot heartbeat. Close Mission Planner/QGC if it owns the serial port; check radio pairing, power and MAVLink baud.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--server', default='https://api.droneops.click')
    parser.add_argument('--new-pairing', action='store_true', help='Replace the saved trusted gateway registration using a new web code')
    args = parser.parse_args()
    from serial.tools import list_ports
    from pymavlink import mavutil
    print('DroneOps · connect USB ground radio · keep this window open')
    port = choose_port(sorted(list_ports.comports(), key=lambda item: item.device))
    print('Detecting autopilot and radio baud…')
    baud, system, component, identity = detect_autopilot(port, mavutil)
    print('Drone hardware:', identity)
    session = Path(__file__).with_name('.sessions') / (identity + '.private.json')
    command = [sys.executable, str(Path(__file__).with_name('gateway.py')), '--server', args.server,
               '--device', port, '--baud', str(baud), '--system-id', str(system), '--component-id', str(component),
               '--session-file', str(session), '--remember-device']
    if args.new_pairing:
        command.append('--new-pairing')
    return subprocess.call(command)


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (RuntimeError, OSError) as error:
        print('Connection stopped:', error, file=sys.stderr)
        raise SystemExit(1)
