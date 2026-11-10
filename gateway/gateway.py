#!/usr/bin/env python3
"""DroneOps gateway. Telemetry by default; flight commands require explicit opt-in."""
import argparse
import datetime as dt
import getpass
import json
import math
import os
from pathlib import Path
import queue
import re
import socket
import sys
import threading
import time
import urllib.parse
import uuid
from flight_control import ControlBridge, FlightControl


class GatewayError(Exception):
    pass


class GatewayWaiting(Exception):
    """A trusted gateway is enrolled but no mission has been selected on the web."""


def gps_signal_status(satellites):
    if satellites == 255:
        return 'UNKNOWN', '33'
    return ('NORMAL', '32') if satellites >= 4 else ('WEAK', '31')


def terminal_color(text, color):
    return text if 'NO_COLOR' in os.environ else '\033[1;%sm%s\033[0m' % (color, text)


def request_required_streams(link, system_id, component_id, mavutil):
    """Request the telemetry used by DroneOps through serial or MAVLink Router."""
    intervals = (
        (mavutil.mavlink.MAVLINK_MSG_ID_GPS_RAW_INT, 500000),
        (mavutil.mavlink.MAVLINK_MSG_ID_GLOBAL_POSITION_INT, 200000),
        (mavutil.mavlink.MAVLINK_MSG_ID_SYS_STATUS, 1000000),
        (mavutil.mavlink.MAVLINK_MSG_ID_BATTERY_STATUS, 1000000),
        (mavutil.mavlink.MAVLINK_MSG_ID_MISSION_CURRENT, 1000000),
    )
    for message_id, interval_us in intervals:
        link.mav.command_long_send(
            system_id, component_id, mavutil.mavlink.MAV_CMD_SET_MESSAGE_INTERVAL,
            0, message_id, interval_us, 0, 0, 0, 0, 0)
    return intervals


def websocket_endpoint(value, allow_insecure=False):
    p = urllib.parse.urlsplit(value)
    if p.scheme not in ('wss', 'ws', 'https', 'http') or not p.hostname or p.username or p.password or p.path not in ('', '/') or p.query or p.fragment:
        raise GatewayError('Server must be a backend origin, e.g. https://api.example.com or wss://api.example.com')
    insecure = p.scheme in ('ws', 'http')
    if insecure and p.hostname not in ('localhost', '127.0.0.1', '::1') and not allow_insecure:
        raise GatewayError('Use WSS, or explicitly --allow-insecure-websocket on a trusted development LAN.')
    scheme = 'wss' if p.scheme in ('wss', 'https') else 'ws'
    return urllib.parse.urlunsplit((scheme, p.netloc, '/ws/drone-gateway', '', ''))


def open_gateway_socket(endpoint, session=None, pairing=None):
    import websocket
    connection = websocket.create_connection(endpoint, timeout=10, enable_multithread=True)
    try:
        if session:
            if session.get('deviceToken'):
                hello = {'type': 'deviceResume', 'payload': {
                    'uavId': session['uavId'], 'deviceToken': session['deviceToken'],
                    'hardwareIdentity': session['hardwareIdentity']}}
                expected = 'session'
            else:
                hello = {'type': 'resume', 'payload': {
                    'connectionId': session['connectionId'], 'token': session['token'],
                    'hardwareIdentity': session['hardwareIdentity']}}
                expected = 'ready'
        else:
            hello = {'type': 'pair', 'payload': pairing}
            expected = 'session'
        connection.send(json.dumps(hello, separators=(',', ':')))
        response = json.loads(connection.recv())
        if response.get('type') == 'waiting':
            raise GatewayWaiting(response.get('message', 'Waiting for mission selection on the web.'))
        if response.get('type') == 'error':
            raise GatewayError(response.get('message', 'Gateway WebSocket handshake rejected.'))
        if response.get('type') != expected:
            raise GatewayError('Backend returned an unexpected WebSocket handshake response.')
        if session and session.get('deviceToken'):
            credential = response.get('payload') or {}
            if credential.get('uavId') != session['uavId'] or credential.get('hardwareIdentity') != session['hardwareIdentity']:
                raise GatewayError('Backend resumed a different UAV or hardware identity.')
            # A device credential is returned only during first enrollment.
            session.update({key: value for key, value in credential.items() if key != 'deviceToken'})
        if session is not None:
            session['executionProtocol'] = response.get('executionProtocol', 0)
        return connection, response.get('payload')
    except BaseException:
        connection.close()
        raise


def exchange(connection, message, expected):
    connection.send(json.dumps(message, separators=(',', ':')))
    response = json.loads(connection.recv())
    if response.get('type') == 'error':
        raise GatewayError(response.get('message', 'Gateway message rejected.'))
    if response.get('type') != expected:
        raise GatewayError('Backend returned an unexpected WebSocket acknowledgement.')
    return response


def save_session(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix('.tmp')
    fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    try:
        os.chmod(temporary, 0o600)
        with os.fdopen(fd, 'w') as stream:
            json.dump(value, stream)
        os.replace(temporary, path)
    finally:
        if temporary.exists():
            temporary.unlink()


def hardware_identity(message):
    """UID is read from AUTOPILOT_VERSION, never from a port/name/system ID."""
    uid = int(getattr(message, 'uid', 0))
    if 0 < uid < 2**64:
        return 'UID64-%016X' % uid
    uid2 = bytes(getattr(message, 'uid2', []))
    if len(uid2) == 18 and any(uid2):
        return 'UID2-' + uid2.hex().upper()
    return None


def read_identity(link, system_id, component_id, mavutil, timeout=30):
    deadline, requested, heartbeat, identity = time.monotonic() + timeout, float('-inf'), False, None
    while time.monotonic() < deadline:
        message = link.recv_match(blocking=True, timeout=.5)
        if message is None or message.get_srcSystem() != system_id or message.get_srcComponent() != component_id:
            continue
        if message.get_type() == 'HEARTBEAT' and message.type != 6 and message.autopilot != 8:
            heartbeat = True
        if heartbeat and time.monotonic() - requested > 2:
            link.mav.command_long_send(system_id, component_id, mavutil.mavlink.MAV_CMD_REQUEST_MESSAGE,
                                       0, mavutil.mavlink.MAVLINK_MSG_ID_AUTOPILOT_VERSION, 0, 0, 0, 0, 0, 0)
            requested = time.monotonic()
        if message.get_type() == 'AUTOPILOT_VERSION':
            identity = hardware_identity(message)
        if heartbeat and identity:
            return identity
    raise GatewayError('No stable autopilot hardware UID received. Enable AUTOPILOT_VERSION forwarding/support; system ID or serial port cannot substitute for hardware identity.')


class TelemetryState:
    def __init__(self, system_id, component_id=1, clock=time.monotonic):
        self.system_id, self.component_id, self.clock = system_id, component_id, clock
        self.values = {}
        self.heartbeat_at = None
        self.gps = None
        self.revision = 0

    def accept(self, message, mode):
        if message.get_srcSystem() != self.system_id or message.get_srcComponent() != self.component_id:
            return False
        kind = message.get_type()
        now = self.clock()
        fields = {}
        if kind == 'HEARTBEAT':
            # GCS/invalid autopilot heartbeats are not evidence of a drone link.
            if message.type == 6 or message.autopilot == 8:
                return False
            self.heartbeat_at = now
            fields = {'armed': bool(message.base_mode & 128), 'flightMode': mode(message)}
        elif kind == 'GLOBAL_POSITION_INT':
            lat, lon = message.lat / 1e7, message.lon / 1e7
            fields = {'altitudeMeters': message.relative_alt / 1000, 'altitudeReference': 'Relative',
                      'altitudeMslMeters': message.alt / 1000 if getattr(message, 'alt', None) is not None else None}
            # Treat the all-zero startup position as unavailable, not a live fix.
            if -90 <= lat <= 90 and -180 <= lon <= 180 and (lat != 0 or lon != 0):
                fields.update(latitude=lat, longitude=lon)
            else:
                fields.update(latitude=None, longitude=None)
            if abs(message.vx) != 32767 and abs(message.vy) != 32767:
                fields['groundSpeedMetersPerSecond'] = math.hypot(message.vx, message.vy) / 100
            fields['headingDegrees'] = None if message.hdg == 65535 else message.hdg / 100
        elif kind == 'GPS_RAW_INT':
            lat, lon = message.lat / 1e7, message.lon / 1e7
            valid = 3 <= message.fix_type <= 8 and -90 <= lat <= 90 and -180 <= lon <= 180 and (lat != 0 or lon != 0)
            self.gps = (lat if valid else None, lon if valid else None,
                        message.fix_type, message.satellites_visible, now)
        elif kind == 'SYS_STATUS':
            fields = {'batteryPercent': message.battery_remaining if 0 <= message.battery_remaining <= 100 else None,
                      'batteryVoltageVolts': None if message.voltage_battery == 65535 else message.voltage_battery / 1000,
                      'batteryCurrentAmps': None if getattr(message, 'current_battery', -1) == -1 else message.current_battery / 100}
            # Prefer fresh, battery-specific readings over the ambiguous system summary.
            fields = {key: value for key, value in fields.items()
                      if now - getattr(self, 'battery_status_at', -math.inf) > 5}
        elif kind == 'BATTERY_STATUS':
            # The Power card represents primary battery ID 0; never mix packs.
            if message.id != 0:
                return False
            self.battery_status_at = now
            volts = list(message.voltages)
            known = [voltage / 1000 for voltage in volts if voltage != 65535]
            extended = [voltage / 1000 for voltage in getattr(message, 'voltages_ext', []) if voltage not in (0, 65535)]
            # A single voltage followed by UINT16_MAX may be whole-pack voltage.
            cells = known + extended if len(known) > 1 else []
            fields = {
                'batteryPercent': message.battery_remaining if 0 <= message.battery_remaining <= 100 else None,
                'batteryVoltageVolts': sum(known + extended) if known else None,
                'batteryCurrentAmps': None if message.current_battery == -1 else message.current_battery / 100,
                'batteryTemperatureCelsius': None if message.temperature == 32767 else message.temperature / 100,
                'batteryConsumedMah': None if message.current_consumed < 0 else message.current_consumed,
                'batteryCellVoltagesVolts': cells or None,
            }
        elif kind == 'MISSION_CURRENT':
            # MAVLink seq is zero-based; web progress contract uses one-based nodes.
            fields = {'missionSequence': message.seq + 1 if message.seq < 65535 else None}
        else:
            return False
        for name, value in fields.items():
            self.values[name] = (value, now)
        self.revision += 1
        return True

    def sample(self, session):
        now = self.clock()
        if self.heartbeat_at is None or now - self.heartbeat_at > 5:
            return None
        value = {key: (item if now - seen <= 5 else None) for key, (item, seen) in self.values.items()}
        # Prefer the fused global estimate; use fresh raw GPS only with a 3D fix.
        # Raw GPS altitude is MSL and must not replace relative altitude.
        if (value.get('latitude') is None or value.get('longitude') is None) and self.gps:
            lat, lon, fix, satellites, seen = self.gps
            if now - seen <= 5 and lat is not None and lon is not None:
                value.update(latitude=lat, longitude=lon)
        value.update(sourceId=session['sourceId'], uavId=session['uavId'], missionId=session['missionId'],
                     sourceTimestamp=dt.datetime.now(dt.timezone.utc).isoformat(), altitudeReference='Relative')
        return value


def forward(endpoint, session, pending, stop, failures, controls=None):
    last_heartbeat, retry_delay = 0.0, 1.0
    inflight = None
    while not stop.is_set():
        connection = None
        try:
            previous_scope = (session.get('connectionId'), session.get('missionId'), session.get('sourceId'))
            connection, _ = open_gateway_socket(endpoint, session=session)
            if controls:
                controls.reconnect()
            # A web-selected mission may have changed while offline. Discard samples
            # captured for the old lease before accepting the new scoped session.
            if previous_scope != (session.get('connectionId'), session.get('missionId'), session.get('sourceId')):
                inflight = None
                while True:
                    try:
                        pending.get_nowait()
                    except queue.Empty:
                        break
            retry_delay = 1.0
            last_heartbeat = 0.0
            last_control = 0.0
            while not stop.is_set():
                if controls and session.get('executionProtocol') == 1 and time.monotonic() - last_control >= 1:
                    payload = controls.payload()
                    if payload:
                        response = exchange(connection, {'type': 'controlPoll', 'payload': payload}, 'controlAck')
                        controls.receive(response['payload'], payload)
                    last_control = time.monotonic()
                if time.monotonic() - last_heartbeat >= 10:
                    exchange(connection, {'type': 'heartbeat'}, 'heartbeatAck')
                    last_heartbeat = time.monotonic()
                if inflight is None:
                    try:
                        inflight = pending.get(timeout=.5)
                    except queue.Empty:
                        continue
                key, sample, captured = inflight
                if time.monotonic() - captured > 10:
                    inflight = None
                    continue
                exchange(connection, {'type': 'telemetry', 'idempotencyKey': key, 'payload': sample}, 'telemetryAck')
                inflight = None
        except GatewayWaiting as error:
            print(str(error), flush=True)
            stop.wait(min(retry_delay, 30))
            retry_delay = min(retry_delay * 2, 30)
        except GatewayError as error:
            if session.get('deviceToken') and str(error).startswith('Connection expired, revoked'):
                # A new mission selection revokes the old lease; the device key
                # may claim only the owner's new, explicitly created connection.
                stop.wait(2)
                continue
            failures.append(str(error))
            stop.set()
        except Exception as error:
            print('Backend WebSocket unavailable (%s); reconnecting. No old data is replayed as live.' % type(error).__name__)
            stop.wait(retry_delay)
            retry_delay = min(retry_delay * 2, 8)
        finally:
            if connection:
                connection.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--server', '--api', dest='server', required=True, help='Backend HTTPS/WSS origin')
    parser.add_argument('--device', required=True, help='udpin:127.0.0.1:14550, COM3 or /dev/ttyUSB0')
    parser.add_argument('--baud', type=int, default=57600)
    parser.add_argument('--system-id', type=int, required=True)
    parser.add_argument('--component-id', type=int, default=1)
    parser.add_argument('--name', default=socket.gethostname())
    parser.add_argument('--session-file', default=str(Path(__file__).with_name('session.private.json')))
    parser.add_argument('--new-pairing', action='store_true')
    parser.add_argument('--remember-device', action='store_true', help='Enroll this trusted gateway for unattended boots and web-selected mission changes')
    parser.add_argument('--pair-only', action='store_true', help='Save a paired session and exit without forwarding')
    parser.add_argument('--service', action='store_true', help='Never prompt interactively; wait for a saved session')
    parser.add_argument('--enable-flight-control', action='store_true', help='Experimental: enable FC mission upload and pilot-authorized commands after SITL/failsafe validation')
    parser.add_argument('--pair-code-stdin', action='store_true', help='Read pairing code from stdin for automated integration tests')
    parser.add_argument('--allow-insecure-websocket', '--allow-insecure-http', dest='allow_insecure_websocket', action='store_true')
    args = parser.parse_args()
    if args.service and (args.new_pairing or args.pair_only):
        raise GatewayError('Service mode cannot create a pairing.')
    while args.service and not Path(args.session_file).exists():
        print('Ready for setup. Run sudo /opt/droneops-gateway/pair-jetson.sh to pair.', flush=True)
        time.sleep(10)
    endpoint = websocket_endpoint(args.server, args.allow_insecure_websocket)
    if not 1 <= args.system_id <= 255 or not 1 <= args.component_id <= 255 or args.baud <= 0:
        raise GatewayError('Use valid MAVLink system/component IDs and baud rate.')
    if not args.device.startswith('udpin:') and not (args.device.startswith('/dev/') or re.fullmatch(r'(?:\\\\\.\\)?COM[0-9]+', args.device, re.IGNORECASE)):
        raise GatewayError('Use udpin:HOST:PORT, /dev/ttyUSB0 or COM3; telemetry log files are not live devices.')
    os.environ.setdefault('MAVLINK20', '1')
    from pymavlink import mavutil
    link = mavutil.mavlink_connection(args.device, baud=args.baud, autoreconnect=True, source_system=250, source_component=190)
    try:
        identity = read_identity(link, args.system_id, args.component_id, mavutil)
    except BaseException:
        link.close()
        raise
    print('Detected drone hardware: ' + identity)
    session = None
    if not args.new_pairing and Path(args.session_file).exists():
        candidate = json.loads(Path(args.session_file).read_text())
        if candidate.get('endpoint') == endpoint and candidate.get('systemId') == args.system_id and candidate.get('componentId') == args.component_id:
            if candidate.get('hardwareIdentity') != identity:
                link.close()
                raise GatewayError('This connected drone is not the device bound to the saved session. Select its registered UAV on the web and pair it separately.')
            session = candidate
    if session is None:
        if args.service:
            raise GatewayError('Saved session does not match this server or device. Pair again.')
        code = sys.stdin.readline().strip() if args.pair_code_stdin else getpass.getpass('One-time pairing code from the web: ')
        connection, session = open_gateway_socket(endpoint, pairing={'code': code, 'gatewayName': args.name,
                          'systemId': args.system_id, 'componentId': args.component_id, 'hardwareIdentity': identity,
                          'rememberGateway': args.remember_device})
        connection.close()
        session.update(endpoint=endpoint, systemId=args.system_id, componentId=args.component_id)
        save_session(args.session_file, session)
    if args.pair_only:
        link.close()
        print('Pairing saved. The background service can now resume this session.')
        return
    print('Gateway paired. Listening for autopilot %s/%s on %s. Flight control: %s.' % (args.system_id, args.component_id, args.device, 'EXPERIMENTAL OPT-IN' if args.enable_flight_control else 'disabled'))
    stop, pending, failures = threading.Event(), queue.Queue(maxsize=1), []
    controls = ControlBridge()
    flight = FlightControl(args.system_id, args.component_id, controls, args.enable_flight_control)
    worker = threading.Thread(target=forward, args=(endpoint, session, pending, stop, failures, controls), daemon=True)
    worker.start()
    streams_requested = False
    state, last_sent, sent_revision, link = TelemetryState(args.system_id, args.component_id), 0.0, -1, link
    last_gps_report = time.monotonic()
    version_requested = float('-inf')
    identity_seen = time.monotonic()
    control_requested = float('-inf')
    try:
        while not stop.is_set():
            try:
                if link is None:
                    link = mavutil.mavlink_connection(args.device, baud=args.baud, autoreconnect=True, source_system=250, source_component=190)
                    if read_identity(link, args.system_id, args.component_id, mavutil) != identity:
                        raise GatewayError('A different physical drone was connected. Telemetry forwarding stopped.')
                    identity_seen = time.monotonic()
                    flight = FlightControl(args.system_id, args.component_id, controls, args.enable_flight_control)
                    controls.reconnect()
                message = link.recv_match(blocking=True, timeout=.5)
                if message:
                    flight.accept(message, link)
                    if message.get_srcSystem() == args.system_id and message.get_srcComponent() == args.component_id:
                        if message.get_type() == 'AUTOPILOT_VERSION':
                            if hardware_identity(message) != identity:
                                raise GatewayError('Drone hardware changed. Telemetry forwarding stopped.')
                            identity_seen = time.monotonic()
                        if message.get_type() == 'HEARTBEAT' and time.monotonic() - version_requested > 5:
                            link.mav.command_long_send(args.system_id,args.component_id,mavutil.mavlink.MAV_CMD_REQUEST_MESSAGE,
                                0,mavutil.mavlink.MAVLINK_MSG_ID_AUTOPILOT_VERSION,0,0,0,0,0,0)
                            version_requested = time.monotonic()
                        if message.get_type() == 'HEARTBEAT' and time.monotonic() - control_requested > 5:
                            # Request read-only evidence even with command execution disabled.
                            link.mav.command_long_send(args.system_id, args.component_id, 511,
                                0, 245, 1000000, 0, 0, 0, 0, 0)  # EXTENDED_SYS_STATE
                            link.mav.command_long_send(args.system_id, args.component_id, 512,
                                0, 242, 0, 0, 0, 0, 0, 0)  # HOME_POSITION may be event-only.
                            control_requested = time.monotonic()
                    accepted = state.accept(message, mavutil.mode_string_v10)
                    if accepted and message.get_type() == 'HEARTBEAT' and not streams_requested:
                        # MAVLink Router is bidirectional, so interval requests work
                        # for both direct serial and the Jetson udpin endpoint.
                        request_required_streams(link, args.system_id, args.component_id, mavutil)
                        streams_requested = True
                        print('Requested MAVLink streams: GPS_RAW_INT 2Hz, GLOBAL_POSITION_INT 5Hz, '
                              'SYS_STATUS 1Hz, BATTERY_STATUS 1Hz, MISSION_CURRENT 1Hz.', flush=True)
                flight.tick(link)
                if time.monotonic() - last_gps_report >= 10:
                    if state.gps is None:
                        print('%s: no GPS_RAW_INT received; waiting for receiver telemetry.' %
                              terminal_color('GPS UNKNOWN', '33'), flush=True)
                    else:
                        lat, lon, fix, satellites, seen = state.gps
                        satellite_text = 'unknown' if satellites == 255 else str(satellites)
                        status, color = gps_signal_status(satellites)
                        print('%s: fix_type=%s satellites_visible=%s age=%.1fs coordinates=%s' %
                              (terminal_color('GPS ' + status, color), fix, satellite_text, time.monotonic() - seen,
                               'available' if lat is not None else 'unavailable'), flush=True)
                    last_gps_report = time.monotonic()
                if time.monotonic() - last_sent >= 1 and state.revision != sent_revision and time.monotonic() - identity_seen <= 15:
                    sample = state.sample(session)
                    if sample is not None:
                        entry = (str(uuid.uuid4()), sample, time.monotonic())
                        try:
                            pending.put_nowait(entry)
                        except queue.Full:
                            try:
                                pending.get_nowait()
                            except queue.Empty:
                                pass
                            pending.put_nowait(entry)
                        sent_revision, last_sent = state.revision, time.monotonic()
            except (OSError, IOError) as error:
                print('MAVLink link unavailable; reconnecting.')
                if link:
                    link.close()
                link = None
                streams_requested = False
                state = TelemetryState(args.system_id, args.component_id)
                stop.wait(2)
    except KeyboardInterrupt:
        print('Gateway stopped. Revoke the session on the web to disconnect permanently.')
    finally:
        stop.set()
        if link:
            link.close()
        worker.join(timeout=10)
    if failures:
        raise GatewayError(failures[0])


if __name__ == '__main__':
    try:
        main()
    except (GatewayError, ImportError, ValueError) as error:
        print('Gateway error:', error)
        raise SystemExit(1)
