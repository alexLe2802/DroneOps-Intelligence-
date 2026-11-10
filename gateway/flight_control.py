"""Experimental, opt-in command executor. All MAVLink IO stays on the reader thread.

Never retries ARM/DISARM/START. A new WebSocket instance invalidates upload and
countdown evidence. RC/GCS arming does not grant permission to start a mission.
"""
import json
import os
from pathlib import Path
import math
import datetime as dt
import queue
import threading
import time
import uuid
from arducopter_recovery import REQUIRED as ARDUCOPTER_RECOVERY_PARAMETERS
from envelope_verifier import EnvelopeVerifier
from arducopter47_adapter import ProfileError, canonical_mission_item


class ControlBridge:
    def __init__(self):
        self.lock = threading.Lock()
        self.instance = str(uuid.uuid4())
        self.evidence = None
        self.evidence_at = 0
        self.responses = queue.Queue()
        self.report = None

    def reconnect(self):
        with self.lock:
            self.instance = str(uuid.uuid4())
            self.evidence = None
            self.evidence_at = 0
            self.report = None

    def payload(self):
        with self.lock:
            if not self.evidence:
                return None
            evidence = self.evidence
            if time.monotonic() - self.evidence_at >= 3:
                evidence = dict(evidence, armed=None, landedState=None, navigationReady=False, preArmReady=False,
                    homeLatitudeDegrees=None, homeLongitudeDegrees=None, homeAltitudeMslMeters=None, verifiedEnvelopeHash=None)
            return {'evidence': dict(evidence, gatewayInstance=self.instance), 'report': self.report}

    def receive(self, response, payload):
        with self.lock:
            if self.report == payload.get('report'):
                self.report = None
            self.responses.put((payload['evidence']['gatewayInstance'], time.monotonic(), response))


class FlightControl:
    def __init__(self, system, component, bridge, enabled=False, clock=time.monotonic):
        self.system, self.component, self.bridge = system, component, bridge
        self.enabled, self.clock = enabled, clock
        self.seen = {}
        self.instance = None
        self.uploaded_hash = None
        self.active = None
        self.lease = 0
        self.arm_at = None
        self.items = []
        self.phase = None
        self.download = {}
        self.ack = False
        self.requested = set()
        self.journal_path = Path(os.environ.get('DRONEOPS_COMMAND_JOURNAL', str(Path.home() / '.droneops-command-journal.json')))
        self.journal_valid = True
        try:
            self.handled = set(json.loads(self.journal_path.read_text())) if self.journal_path.exists() else set()
        except (OSError, ValueError, TypeError):
            self.handled = set()
            self.journal_valid = False
        self.reached = set()
        self.observed_hash = None
        self.landed_observed_at = None
        self.last_gcs_heartbeat = float('-inf')
        self.parameters = {}
        self.parameters_requested = float('-inf')
        self.envelope = EnvelopeVerifier(system, component, clock)

    def fresh(self, kind, seconds=3):
        item = self.seen.get(kind)
        return item[0] if item and self.clock() - item[1] <= seconds else None

    def evidence(self):
        hb, landed, health, gps = (self.fresh(k) for k in ('HEARTBEAT', 'EXTENDED_SYS_STATE', 'SYS_STATUS', 'GPS_RAW_INT'))
        version, home = self.fresh('AUTOPILOT_VERSION', 15), self.fresh('HOME_POSITION', 15)
        firmware = {3: 'ArduPilot', 12: 'PX4'}.get(hb.autopilot) if hb else None
        valid_home = bool(home and -900000000 <= home.latitude <= 900000000 and -1800000000 <= home.longitude <= 1800000000
                          and (home.latitude != 0 or home.longitude != 0))
        # Only multicopters with MISSION_INT capability are accepted by this profile.
        navigation = bool(hb and hb.type in (2, 3, 4, 13, 14, 15, 29, 35) and gps and 3 <= gps.fix_type <= 8
                          and version and version.capabilities & 4 and valid_home
                          and health and health.onboard_control_sensors_enabled != 0
                          and (health.onboard_control_sensors_health & health.onboard_control_sensors_enabled)
                          == health.onboard_control_sensors_enabled)
        prearm = bool(navigation and health.onboard_control_sensors_enabled & (1 << 28)
                      and health.onboard_control_sensors_health & (1 << 28))
        parameters = {name: value for name, (value, seen, utc) in self.parameters.items() if self.clock() - seen <= 60}
        version_number = getattr(version, 'flight_sw_version', 0) if version else 0
        parameter_snapshot = None
        if parameters and version_number:
            oldest = min(utc for name, (_, seen, utc) in self.parameters.items() if name in parameters)
            parameter_snapshot = {'firmwareVersion': '%d.%d.%d' % (version_number >> 24, (version_number >> 16) & 255, (version_number >> 8) & 255),
                'values': parameters, 'observedAtUtc': oldest}
        return {'firmware': firmware, 'armed': bool(hb.base_mode & 128) if hb else None,
                'landedState': landed.landed_state if landed else None,
                'navigationReady': navigation, 'preArmReady': prearm, 'uploadedHash': self.uploaded_hash,
                'homeLatitudeDegrees': home.latitude / 1e7 if valid_home else None,
                'homeLongitudeDegrees': home.longitude / 1e7 if valid_home else None,
                'homeAltitudeMslMeters': home.altitude / 1000 if valid_home else None,
                'firmwareParameters': parameter_snapshot, 'verifiedEnvelopeHash': self.envelope.verified_hash,
                'fcProfileError': self.envelope.error,
                'missionCurrent': self.fresh('MISSION_CURRENT', 10).seq if self.fresh('MISSION_CURRENT', 10) else None,
                'missionMode': bool(hb and mission_mode(hb)), 'reachedCommands': sorted(self.reached),
                'landedObservedAtUtc': self.landed_observed_at}

    def request_parameters(self, link):
        if self.clock() - self.parameters_requested < 20:
            return
        heartbeat = self.fresh('HEARTBEAT')
        if not heartbeat:
            return
        names = ('WPNAV_SPEED', 'WPNAV_SPEED_UP', 'WPNAV_SPEED_DN', 'WPNAV_RADIUS', 'LAND_SPEED',
                 'WP_SPD', 'WP_SPD_UP', 'WP_SPD_DN', 'WP_RADIUS_M', 'LAND_SPD_MS', 'LAND_SPD_HIGH_MS', 'RTL_ALT_M', 'RTL_SPEED_MS', 'BATT_CAPACITY') if heartbeat.autopilot == 3 else (
                 'MPC_XY_CRUISE', 'MPC_XY_VEL_MAX', 'MPC_Z_V_AUTO_UP', 'MPC_Z_V_AUTO_DN',
                 'MPC_TKO_SPEED', 'MPC_LAND_SPEED', 'NAV_ACC_RAD', 'GF_MAX_VER_DIST', 'BAT1_CAPACITY') if heartbeat.autopilot == 12 else ()
        if heartbeat.autopilot == 3:
            names = tuple(dict.fromkeys((*names, *ARDUCOPTER_RECOVERY_PARAMETERS, 'ARMING_SKIPCHK')))
        for name in names:
            link.mav.param_request_read_send(self.system, self.component, name.encode('ascii'), -1)
        self.parameters_requested = self.clock()

    def grounded(self):
        evidence = self.evidence()
        return evidence['landedState'] == 1 and evidence['armed'] is not None

    def finish(self, succeeded):
        if self.active:
            with self.bridge.lock:
                self.bridge.report = {'commandId': self.active['id'], 'succeeded': succeeded,
                                      'uploadedHash': self.uploaded_hash}
        self.active, self.phase, self.ack = None, None, False

    def accept(self, message, link):
        if message.get_srcSystem() != self.system or message.get_srcComponent() != self.component:
            return
        kind = message.get_type()
        if self.envelope.phase and (self.clock() >= self.lease or not self.grounded()
                or self.evidence()['armed'] is not False or not self.evidence()['navigationReady']):
            self.envelope.fail('Fresh ground/navigation evidence and an execution lease are required during FC configuration.')
        self.envelope.accept(message, link)
        if kind == 'EXTENDED_SYS_STATE':
            self.landed_observed_at = dt.datetime.now(dt.timezone.utc).isoformat()
        if kind == 'PARAM_VALUE':
            name = message.param_id.decode('ascii', errors='ignore') if isinstance(message.param_id, bytes) else message.param_id
            name = name.split('\0')[0]
            allowed = {'WPNAV_SPEED', 'WPNAV_SPEED_UP', 'WPNAV_SPEED_DN', 'WPNAV_RADIUS', 'LAND_SPEED', 'WP_SPD', 'WP_SPD_UP', 'WP_SPD_DN', 'WP_RADIUS_M', 'LAND_SPD_MS', 'LAND_SPD_HIGH_MS', 'RTL_ALT_M', 'RTL_SPEED_MS',
                'MPC_XY_CRUISE', 'MPC_XY_VEL_MAX', 'MPC_Z_V_AUTO_UP', 'MPC_Z_V_AUTO_DN', 'MPC_TKO_SPEED', 'MPC_LAND_SPEED', 'NAV_ACC_RAD', 'GF_MAX_VER_DIST', 'BATT_CAPACITY', 'BAT1_CAPACITY'}
            allowed.update((*ARDUCOPTER_RECOVERY_PARAMETERS, 'ARMING_SKIPCHK'))
            if name in allowed and math.isfinite(message.param_value):
                self.parameters[name] = (message.param_value, self.clock(), dt.datetime.now(dt.timezone.utc).isoformat())
        if kind == 'HEARTBEAT' and (message.type == 6 or message.autopilot == 8):
            return
        if kind in ('HEARTBEAT', 'EXTENDED_SYS_STATE', 'SYS_STATUS', 'GPS_RAW_INT', 'AUTOPILOT_VERSION', 'HOME_POSITION', 'MISSION_CURRENT', 'GLOBAL_POSITION_INT'):
            self.seen[kind] = (message, self.clock())
        if kind == 'MISSION_ITEM_REACHED' and self.uploaded_hash and 0 <= message.seq < len(self.items):
            self.reached.add(message.seq)
        # Other GCS changes invalidate verification, even while this gateway is idle.
        if kind in ('MISSION_COUNT', 'MISSION_ACK') and self.active is None:
            self.uploaded_hash = None
        if not self.active:
            return
        if kind in ('MISSION_REQUEST', 'MISSION_REQUEST_INT', 'MISSION_ACK', 'MISSION_COUNT', 'MISSION_ITEM_INT', 'COMMAND_ACK'):
            if getattr(message, 'target_system', link.source_system) not in (0, link.source_system):
                return
            if getattr(message, 'target_component', link.source_component) not in (0, link.source_component):
                return
        if getattr(message, 'mission_type', 0) != 0:
            return
        action = self.active['action']
        if action == 'Upload':
            if kind in ('MISSION_REQUEST', 'MISSION_REQUEST_INT') and self.phase == 'upload':
                if not 0 <= message.seq < len(self.items):
                    self.finish(False)
                    return
                item = self.items[message.seq]
                self.requested.add(message.seq)
                link.mav.mission_item_int_send(self.system, self.component, item['seq'], item['frame'], item['command'],
                    0, 1, item['param1'], item['param2'], item['param3'],
                    float('nan') if item['param4'] is None else item['param4'], item['x'], item['y'], item['z'], 0)
            elif kind == 'MISSION_ACK' and self.phase == 'upload':
                if message.type != 0 or len(self.requested) != len(self.items):
                    self.finish(False)
                else:
                    self.phase = 'download_count'
                    link.mav.mission_request_list_send(self.system, self.component, 0)
            elif kind == 'MISSION_COUNT' and self.phase == 'download_count':
                if message.count != len(self.items):
                    self.finish(False)
                else:
                    self.phase = 'download'
                    link.mav.mission_request_int_send(self.system, self.component, 0, 0)
            elif kind == 'MISSION_ITEM_INT' and self.phase == 'download':
                expected = len(self.download)
                if message.seq != expected:
                    return
                if not same_item(self.items[expected], message):
                    self.finish(False)
                    return
                self.download[expected] = True
                if len(self.download) == len(self.items):
                    link.mav.mission_ack_send(self.system, self.component, 0, 0)
                    self.uploaded_hash = self.active['compiledHash']
                    self.finish(True)
                else:
                    link.mav.mission_request_int_send(self.system, self.component, expected + 1, 0)
        else:
            command = 300 if action == 'Start' else 400
            if kind == 'COMMAND_ACK' and message.command == command:
                if message.result == 5:  # IN_PROGRESS is not success.
                    return
                if message.result != 0:
                    self.finish(False)
                    return
                self.ack = True
            # ACK alone does not prove motor state. Require a subsequent FC heartbeat.
            if kind == 'HEARTBEAT' and self.ack:
                armed = bool(message.base_mode & 128)
                if action == 'Arm' and armed and self.grounded():
                    self.arm_at = self.clock()
                    self.finish(True)
                elif action == 'Disarm' and not armed and self.grounded():
                    self.arm_at = None
                    self.finish(True)
                elif action == 'Start' and armed and mission_mode(message):
                    self.arm_at = None
                    self.finish(True)

    def tick(self, link):
        now = self.clock()
        self.request_parameters(link)
        with self.bridge.lock:
            instance = self.bridge.instance
        if self.instance != instance:
            self.instance = instance
            self.finish(False)
            self.arm_at, self.lease = None, 0
            self.envelope.reset()
        while True:
            try:
                source, received, response = self.bridge.responses.get_nowait()
            except queue.Empty:
                break
            if source != instance or now - received >= 3:
                continue
            self.lease = received + min(3, max(0, response.get('leaseSeconds', 0))) if response.get('enabled') else 0
            if response.get('stage') not in ('Arming', 'Countdown', 'Starting'):
                self.arm_at = None
            self.prepare_envelope(link, response)
            command = response.get('command')
            if not command or command['id'] in self.handled:
                continue
            if not self.journal_valid:
                continue
            self.handled.add(command['id'])
            try:
                self.journal_path.parent.mkdir(parents=True, exist_ok=True)
                temporary = self.journal_path.with_suffix('.tmp')
                with temporary.open('w') as stream:
                    json.dump(sorted(self.handled), stream)
                    stream.flush()
                    os.fsync(stream.fileno())
                temporary.replace(self.journal_path)
            except OSError:
                self.journal_valid = False
                continue
            if len(self.handled) > 1024:
                # A process restart is needed rather than forgetting command IDs.
                self.enabled = False
            if self.active:
                continue
            self.active = dict(command, compiledHash=response.get('compiledHash'))
            self.deadline = now + (60 if command['action'] == 'Upload' else 8)
            self.ack = False
            self.begin(link, response)
        self.envelope.tick()
        if self.envelope.verified_hash:
            version = self.fresh('AUTOPILOT_VERSION', 15)
            if not version or version.flight_sw_version >> 8 != 0x040701:
                self.envelope.fail('FC firmware evidence changed or expired.')
        if now >= self.lease:
            self.envelope.reset()
        if self.envelope.phase and (not self.grounded() or self.evidence()['armed'] is not False):
            self.envelope.fail('FC must stay disarmed and on ground during parameter application.')
        if now >= self.lease or not self.grounded() or not self.evidence()['navigationReady']:
            self.arm_at = None
            if self.active and self.active['action'] in ('Arm', 'Start'):
                self.finish(False)
        if self.active and now >= self.deadline:
            self.finish(False)
        if self.enabled and now < self.lease and now - self.last_gcs_heartbeat >= 1:
            # Cloud lease loss stops this GCS heartbeat. The validated FC-side GCS
            # failsafe must handle the loss independently of the browser/backend.
            link.mav.heartbeat_send(6, 8, 0, 0, 4)
            self.last_gcs_heartbeat = now
        with self.bridge.lock:
            self.bridge.evidence = self.evidence()
            self.bridge.evidence_at = time.monotonic()

    def prepare_envelope(self, link, response):
        wanted = response.get('requiredEnvelopeHash')
        if wanted != self.envelope.plan_hash:
            self.envelope.reset()
        if not self.enabled or not response.get('enabled') or self.clock() >= self.lease:
            self.envelope.reset()
            return
        if not wanted or not response.get('operatingEnvelope') or self.envelope.phase or self.envelope.verified_hash or self.envelope.failed_hash == wanted:
            return
        evidence = self.evidence()
        snapshot = evidence['firmwareParameters']
        if (not self.grounded() or evidence['armed'] is not False or not evidence['navigationReady']
                or evidence['firmware'] != 'ArduPilot' or not snapshot or snapshot['firmwareVersion'] != '4.7.1'):
            return
        if snapshot['values'].get('ARMING_SKIPCHK') != 0 or (response['operatingEnvelope'].get('configureRecovery') is True and snapshot['values'].get('FS_EKF_ACTION') != 1):
            self.envelope.error = 'All FC arming checks and the reviewed EKF-loss LAND action are required.'
            return
        self.envelope.begin(wanted, response['operatingEnvelope'], snapshot['values'], link)

    def begin(self, link, response):
        evidence = self.evidence()
        action = self.active['action']
        if action in ('Arm', 'Start'):
            version = self.fresh('AUTOPILOT_VERSION', 15)
            uid = int(getattr(version, 'uid', 0)) if version else 0
            uid2 = bytes(getattr(version, 'uid2', [])) if version else b''
            identity = ('UID64-%016X' % uid) if uid else ('UID2-' + uid2.hex().upper() if len(uid2) == 18 and any(uid2) else None)
            if not response.get('missionId') or not response.get('connectionId') or not response.get('planHash') or identity != response.get('controllerUid'):
                self.finish(False)
                return
        if action in ('Arm', 'Start') and response.get('requiredEnvelopeHash') and self.envelope.verified_hash != response['requiredEnvelopeHash']:
            self.finish(False)
            return
        if not self.enabled or not response.get('enabled') or self.clock() >= self.lease or not self.grounded() or not timely_command(response, self.active):
            self.finish(False)
            return
        if action == 'Upload':
            plan = response.get('mission')
            if plan and (plan.get('operationalEnvelope') or {}).get('fcParameterVerificationRequired') and self.envelope.verified_hash != self.active['compiledHash']:
                # The new policy must not be mistaken for an onboard limit. Until
                # a versioned parameter adapter has read-back proof, fail closed.
                print('FC operating-envelope parameter verification is required. Upload/ARM blocked.', flush=True)
                self.finish(False)
                return
            if evidence['armed'] or not plan or plan.get('firmwareTarget') != evidence['firmware']:
                self.finish(False)
                return
            actual_home = self.fresh('HOME_POSITION', 15)
            # Planned Home is editor metadata, never an FC Home command or launch gate.
            if plan.get('operationalEnvelope') and plan.get('items'):
                origin = plan['items'][0]
                takeoff = {'latitude': origin['x'] / 1e7, 'longitude': origin['y'] / 1e7}
                if origin['command'] != 22 or not planned_home_matches(takeoff, actual_home):
                    print('Takeoff waypoint must be within 10 m of FC Home. Upload blocked.', flush=True)
                    self.finish(False)
                    return
            self.reached.clear()
            self.items = [dict(item) for item in plan['items']]
            if not self.items or len(self.items) > 500:
                self.finish(False)
                return
            if evidence['firmware'] == 'ArduPilot':
                try:
                    snapshot = evidence['firmwareParameters']
                    self.items = [canonical_mission_item(item, snapshot['values'] if snapshot else {}) for item in self.items]
                except ProfileError as error:
                    self.envelope.error = str(error)
                    self.finish(False)
                    return
                home = self.fresh('HOME_POSITION', 15)
                if not home:
                    self.finish(False)
                    return
                self.items.insert(0, dict(seq=0, command=16, frame=0, param1=0, param2=0, param3=0, param4=0,
                    x=home.latitude, y=home.longitude, z=home.altitude / 1000))
                for index, item in enumerate(self.items):
                    item['seq'] = index
            self.phase, self.download, self.requested = 'upload', {}, set()
            self.uploaded_hash = None
            link.mav.mission_count_send(self.system, self.component, len(self.items), 0)
        elif action == 'Arm':
            hb = self.fresh('HEARTBEAT')
            # Arming into AUTO could take off before the countdown. Require a
            # non-autonomous holding mode and leave FC arming checks enabled.
            safe_mode = hb and ((hb.autopilot == 3 and hb.custom_mode == 5)
                                or (hb.autopilot == 12 and (hb.custom_mode >> 16) & 255 == 3))
            if not safe_mode or evidence['armed'] or not evidence['preArmReady'] or self.uploaded_hash != self.active['compiledHash']:
                self.finish(False)
                return
            link.mav.command_long_send(self.system, self.component, 400, 0, 1, 0, 0, 0, 0, 0, 0)
        elif action == 'Disarm':
            self.arm_at = None
            if not evidence['armed']:
                self.finish(False)
                return
            link.mav.command_long_send(self.system, self.component, 400, 0, 0, 0, 0, 0, 0, 0, 0)
        elif action == 'Start':
            if not evidence['armed'] or not evidence['navigationReady'] or self.arm_at is None or self.uploaded_hash != self.active['compiledHash']:
                self.finish(False)
                return
            self.arm_at = None  # Consume permission before issuing START. Never repeat.
            link.mav.command_long_send(self.system, self.component, 300, 0, 0, 0, 0, 0, 0, 0, 0)
        else:
            self.finish(False)


def planned_home_matches(planned, actual):
    if actual is None or not isinstance(planned, dict):
        return False
    try:
        lat, lon = planned['latitude'], planned['longitude']
        altitude = planned.get('altitudeMslMeters')
        if any(isinstance(v, bool) or not isinstance(v, (int, float)) or not math.isfinite(v) for v in (lat, lon)):
            return False
        if not -90 <= lat <= 90 or not -180 <= lon <= 180:
            return False
        lat2, lon2 = actual.latitude / 1e7, actual.longitude / 1e7
        if not -90 <= lat2 <= 90 or not -180 <= lon2 <= 180 or (lat2 == 0 and lon2 == 0):
            return False
        a = math.sin(math.radians(lat2 - lat) / 2) ** 2 + math.cos(math.radians(lat)) * math.cos(math.radians(lat2)) * math.sin(math.radians(lon2 - lon) / 2) ** 2
        distance = 6371000 * 2 * math.asin(math.sqrt(min(1, max(0, a))))
        return distance <= 10 and (altitude is None or not isinstance(altitude, bool) and isinstance(altitude, (int, float))
            and math.isfinite(altitude) and abs(altitude - actual.altitude / 1000) <= 5)
    except (KeyError, TypeError, AttributeError):
        return False


def mission_mode(heartbeat):
    return (heartbeat.autopilot == 3 and heartbeat.custom_mode == 3) or (
        heartbeat.autopilot == 12 and (heartbeat.custom_mode >> 16) & 255 == 4
        and (heartbeat.custom_mode >> 24) & 255 == 4)


def timely_command(response, command):
    """Initial delivery validity uses UTC; the countdown uses monotonic time."""
    try:
        now = dt.datetime.now(dt.timezone.utc)
        server = dt.datetime.fromisoformat(response['serverNow'].replace('Z', '+00:00'))
        expiry = dt.datetime.fromisoformat(command['expiresAt'].replace('Z', '+00:00'))
        age = (now - server).total_seconds()
        remaining = (expiry - now).total_seconds()
        return -2 <= age < 3 and 0 < remaining <= (62 if command['action'] == 'Upload' else 10)
    except (KeyError, ValueError, TypeError, AttributeError):
        return False


def same_item(item, message):
    if any(item[field] != getattr(message, field) for field in ('seq', 'command', 'x', 'y')):
        return False
    # Non-navigation frames may be normalized by firmware; navigation frames must match.
    if item['command'] in (16, 21, 22) and item['frame'] != message.frame:
        return False
    for field in ('param1', 'param2', 'param3', 'param4', 'z'):
        expected, actual = item[field], getattr(message, field)
        if expected is None:
            if not math.isnan(actual):
                return False
        elif not math.isfinite(actual) or not math.isclose(expected, actual, rel_tol=1e-6, abs_tol=1e-5):
            return False
    return getattr(message, 'autocontinue', 1) == 1
