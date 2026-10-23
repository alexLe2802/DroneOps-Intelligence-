#!/usr/bin/env python3
"""DroneOps MAVLink telemetry gateway. Reads only; never sends flight commands."""
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
import urllib.error
import urllib.parse
import urllib.request
import uuid


class GatewayError(Exception):
    pass


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None  # Never forward a pairing code/token to a redirected host.


def api_origin(value, allow_http=False):
    p = urllib.parse.urlsplit(value)
    if p.scheme not in ('https', 'http') or not p.hostname or p.username or p.password or p.path not in ('', '/') or p.query or p.fragment:
        raise GatewayError('API must be a backend origin, e.g. https://api.example.com')
    if p.scheme == 'http' and p.hostname not in ('localhost', '127.0.0.1', '::1') and not allow_http:
        raise GatewayError('Use HTTPS, or explicitly --allow-insecure-http on a trusted development LAN.')
    return value.rstrip('/')


def request(api, path, payload, token=None, key=None, identity=None):
    headers = {'Content-Type': 'application/json'}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    if identity:
        headers['X-Drone-Identity'] = identity
    if key:
        headers['Idempotency-Key'] = key
    req = urllib.request.Request(api + path, data=json.dumps(payload).encode(), headers=headers, method='POST')
    with urllib.request.build_opener(NoRedirect).open(req, timeout=8) as response:
        data = response.read()
        return json.loads(data) if data else None


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
            fields = {'altitudeMeters': message.relative_alt / 1000, 'altitudeReference': 'Relative'}
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
                      'batteryVoltageVolts': None if message.voltage_battery == 65535 else message.voltage_battery / 1000}
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


def forward(api, session, pending, stop, failures):
    last_heartbeat, retry_delay = 0.0, 1.0
    while not stop.is_set():
        try:
            if time.monotonic() - last_heartbeat >= 10:
                request(api, '/api/drone-gateway/' + session['connectionId'] + '/heartbeat', {}, session['token'], identity=session['hardwareIdentity'])
                last_heartbeat = time.monotonic()
            try:
                key, sample, captured = pending.get(timeout=.5)
            except queue.Empty:
                continue
            # Drop old samples on reconnect; do not present backlog as live data.
            while not stop.is_set() and time.monotonic() - captured <= 10:
                try:
                    request(api, '/api/drone-gateway/' + session['connectionId'] + '/samples', sample, session['token'], key, session['hardwareIdentity'])
                    retry_delay = 1.0
                    break
                except urllib.error.HTTPError as error:
                    if error.code in (401, 403):
                        raise
                    if error.code < 500 and error.code != 429:
                        print('Sample rejected (HTTP %s). Check mission status, clock and mapping.' % error.code)
                        break
                    stop.wait(retry_delay)
                    retry_delay = min(retry_delay * 2, 8)
                except (OSError, TimeoutError):
                    stop.wait(retry_delay)
                    retry_delay = min(retry_delay * 2, 8)
        except urllib.error.HTTPError as error:
            if error.code in (401, 403):
                failures.append('Connection revoked, expired or no longer eligible. Create a new pairing on the web.')
                stop.set()
            else:
                print('Backend unavailable (HTTP %s); retrying.' % error.code)
                stop.wait(3)
        except (OSError, TimeoutError):
            print('Backend unreachable; retrying. No old data is replayed as live.')
            stop.wait(3)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--api', required=True)
    parser.add_argument('--device', required=True, help='udpin:127.0.0.1:14550, COM3 or /dev/ttyUSB0')
    parser.add_argument('--baud', type=int, default=57600)
    parser.add_argument('--system-id', type=int, required=True)
    parser.add_argument('--component-id', type=int, default=1)
    parser.add_argument('--name', default=socket.gethostname())
    parser.add_argument('--session-file', default=str(Path(__file__).with_name('session.private.json')))
    parser.add_argument('--new-pairing', action='store_true')
    parser.add_argument('--pair-code-stdin', action='store_true', help='Read pairing code from stdin for automated integration tests')
    parser.add_argument('--allow-insecure-http', action='store_true')
    args = parser.parse_args()
    api = api_origin(args.api, args.allow_insecure_http)
    if not 1 <= args.system_id <= 255 or not 1 <= args.component_id <= 255 or args.baud <= 0:
        raise GatewayError('Use valid MAVLink system/component IDs and baud rate.')
    if not args.device.startswith('udpin:') and not (args.device.startswith('/dev/') or re.fullmatch(r'(?:\\\\\.\\)?COM[0-9]+', args.device, re.IGNORECASE)):
        raise GatewayError('Use udpin:HOST:PORT, /dev/ttyUSB0 or COM3; telemetry log files are not live devices.')
    os.environ.setdefault('MAVLINK20', '1')
    from pymavlink import mavutil
    link = mavutil.mavlink_connection(args.device, baud=args.baud, autoreconnect=True)
    try:
        identity = read_identity(link, args.system_id, args.component_id, mavutil)
    except BaseException:
        link.close()
        raise
    print('Detected drone hardware: ' + identity)
    session = None
    if not args.new_pairing and Path(args.session_file).exists():
        candidate = json.loads(Path(args.session_file).read_text())
        if candidate.get('api') == api and candidate.get('systemId') == args.system_id and candidate.get('componentId') == args.component_id:
            if candidate.get('hardwareIdentity') != identity:
                link.close()
                raise GatewayError('This connected drone is not the device bound to the saved session. Select its registered UAV on the web and pair it separately.')
            session = candidate
    if session is None:
        code = sys.stdin.readline().strip() if args.pair_code_stdin else getpass.getpass('One-time pairing code from the web: ')
        session = request(api, '/api/drone-gateway/pair', {'code': code, 'gatewayName': args.name,
                          'systemId': args.system_id, 'componentId': args.component_id, 'hardwareIdentity': identity})
        session.update(api=api, systemId=args.system_id, componentId=args.component_id)
        save_session(args.session_file, session)
    print('Gateway paired. Listening for autopilot %s/%s on %s. No flight commands are sent.' % (args.system_id, args.component_id, args.device))
    stop, pending, failures = threading.Event(), queue.Queue(maxsize=1), []
    worker = threading.Thread(target=forward, args=(api, session, pending, stop, failures), daemon=True)
    worker.start()
    streams_requested = False
    state, last_sent, sent_revision, link = TelemetryState(args.system_id, args.component_id), 0.0, -1, link
    last_gps_report = time.monotonic()
    version_requested = float('-inf')
    identity_seen = time.monotonic()
    try:
        while not stop.is_set():
            try:
                if link is None:
                    link = mavutil.mavlink_connection(args.device, baud=args.baud, autoreconnect=True)
                    if read_identity(link, args.system_id, args.component_id, mavutil) != identity:
                        raise GatewayError('A different physical drone was connected. Telemetry forwarding stopped.')
                    identity_seen = time.monotonic()
                message = link.recv_match(blocking=True, timeout=.5)
                if message:
                    if message.get_srcSystem() == args.system_id and message.get_srcComponent() == args.component_id:
                        if message.get_type() == 'AUTOPILOT_VERSION':
                            if hardware_identity(message) != identity:
                                raise GatewayError('Drone hardware changed. Telemetry forwarding stopped.')
                            identity_seen = time.monotonic()
                        if message.get_type() == 'HEARTBEAT' and time.monotonic() - version_requested > 5:
                            link.mav.command_long_send(args.system_id,args.component_id,mavutil.mavlink.MAV_CMD_REQUEST_MESSAGE,
                                0,mavutil.mavlink.MAVLINK_MSG_ID_AUTOPILOT_VERSION,0,0,0,0,0,0)
                            version_requested = time.monotonic()
                    accepted = state.accept(message, mavutil.mode_string_v10)
                    if accepted and message.get_type() == 'HEARTBEAT' and not streams_requested and not args.device.startswith('udpin:'):
                        # Request telemetry rates on a direct serial link. This is a
                        # data-stream request, not arm/takeoff/mission control.
                        link.mav.request_data_stream_send(args.system_id, args.component_id,
                                                          mavutil.mavlink.MAV_DATA_STREAM_ALL, 2, 1)
                        streams_requested = True
                if time.monotonic() - last_gps_report >= 10:
                    if state.gps is None:
                        print('GPS: no GPS_RAW_INT received; waiting for receiver telemetry.', flush=True)
                    else:
                        lat, lon, fix, satellites, seen = state.gps
                        satellite_text = 'unknown' if satellites == 255 else str(satellites)
                        print('GPS: fix_type=%s satellites=%s age=%.1fs coordinates=%s' %
                              (fix, satellite_text, time.monotonic() - seen,
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
    except urllib.error.HTTPError as error:
        try:
            problem = json.loads(error.read(4096))
            detail = problem.get('message', 'Pairing request rejected')
        except (ValueError, OSError):
            detail = str(error)
        print('Gateway error:', ''.join(c for c in str(detail)[:500] if c.isprintable()))
        raise SystemExit(1)
    except (GatewayError, ImportError, ValueError) as error:
        # HTTPError string never includes the bearer token or request body.
        print('Gateway error:', error)
        raise SystemExit(1)
