import datetime as dt
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
import os
import json
import queue
import threading
from unittest.mock import patch, Mock
from gateway import main, forward
from gateway import TelemetryState, websocket_endpoint, GatewayError, save_session, hardware_identity, read_identity

SESSION={'sourceId':'gateway-test','uavId':'uav','missionId':'mission'}
def message(kind, system=1, component=1, **values):
    return SimpleNamespace(get_type=lambda:kind,get_srcSystem=lambda:system,get_srcComponent=lambda:component,**values)

class GatewayTests(unittest.TestCase):
    def setUp(self):
        self.now=0
        self.state=TelemetryState(1,1,lambda:self.now)
    def heartbeat(self, **kwargs):
        self.state.accept(message('HEARTBEAT',type=2,autopilot=3,base_mode=128,**kwargs),lambda m:'AUTO')
    def test_no_heartbeat_no_fake_samples(self):
        self.assertIsNone(self.state.sample(SESSION))
    def test_wrong_system_and_component_are_ignored(self):
        self.assertFalse(self.state.accept(message('HEARTBEAT',system=2),None))
        self.assertFalse(self.state.accept(message('HEARTBEAT',component=2),None))
    def test_gcs_is_not_a_drone(self):
        self.state.accept(message('HEARTBEAT',type=6,autopilot=8,base_mode=0),None)
        self.assertIsNone(self.state.sample(SESSION))
    def test_units_and_metadata(self):
        self.heartbeat()
        self.state.accept(message('GLOBAL_POSITION_INT',lat=113500000,lon=1063400000,relative_alt=50250,vx=300,vy=400,hdg=12345),None)
        value=self.state.sample(SESSION)
        self.assertEqual((value['latitude'],value['longitude'],value['altitudeMeters']),(11.35,106.34,50.25))
        self.assertEqual(value['groundSpeedMetersPerSecond'],5)
        self.assertEqual(value['headingDegrees'],123.45)
        self.assertEqual(value['missionId'],'mission')
        self.assertTrue(value['armed'])
    def test_zero_or_invalid_position_clears_previous_fix_but_keeps_other_telemetry(self):
        self.heartbeat()
        for lat, lon in [(0, 0), (910000000, 10000000)]:
            self.state.accept(message('GLOBAL_POSITION_INT',lat=113500000,lon=1063400000,relative_alt=1000,vx=0,vy=0,hdg=0),None)
            self.state.accept(message('GLOBAL_POSITION_INT',lat=lat,lon=lon,relative_alt=3800,vx=0,vy=0,hdg=0),None)
            value=self.state.sample(SESSION)
            self.assertIsNone(value['latitude'])
            self.assertIsNone(value['longitude'])
            self.assertEqual(value['altitudeMeters'],3.8)

    def test_raw_gps_fallback_requires_fresh_3d_fix_and_preserves_relative_altitude(self):
        self.heartbeat()
        self.state.accept(message('GLOBAL_POSITION_INT',lat=0,lon=0,relative_alt=3800,vx=0,vy=0,hdg=0),None)
        for fix in (0, 1, 2):
            self.state.accept(message('GPS_RAW_INT',lat=107700000,lon=1067000000,fix_type=fix,satellites_visible=5),None)
            self.assertIsNone(self.state.sample(SESSION)['latitude'])
        self.state.accept(message('GPS_RAW_INT',lat=107700000,lon=1067000000,fix_type=3,satellites_visible=9),None)
        value=self.state.sample(SESSION)
        self.assertEqual((value['latitude'],value['longitude'],value['altitudeMeters']),(10.77,106.7,3.8))
        self.state.accept(message('GLOBAL_POSITION_INT',lat=107800000,lon=1067100000,relative_alt=3800,vx=0,vy=0,hdg=0),None)
        self.assertEqual(self.state.sample(SESSION)['latitude'],10.78)
        self.now=6
        self.heartbeat()
        self.assertIsNone(self.state.sample(SESSION)['latitude'])

    def test_missing_fields_and_sentinels_remain_unknown(self):
        self.heartbeat()
        self.state.accept(message('SYS_STATUS',battery_remaining=-1,voltage_battery=65535),None)
        self.assertIsNone(self.state.sample(SESSION)['batteryPercent'])
        self.assertIsNone(self.state.sample(SESSION)['batteryVoltageVolts'])
        self.assertNotIn('latitude',self.state.sample(SESSION))
    def test_loss_of_heartbeat_stops_samples(self):
        self.heartbeat();self.now=6
        self.assertIsNone(self.state.sample(SESSION))
    def test_fresh_heartbeat_does_not_refresh_old_gps(self):
        self.heartbeat()
        self.state.accept(message('GLOBAL_POSITION_INT',lat=10000000,lon=20000000,relative_alt=0,vx=0,vy=0,hdg=65535),None)
        self.now=6;self.heartbeat()
        self.assertIsNone(self.state.sample(SESSION)['latitude'])
    def test_mission_sequence_is_one_based(self):
        self.heartbeat();self.state.accept(message('MISSION_CURRENT',seq=0),None)
        self.assertEqual(self.state.sample(SESSION)['missionSequence'],1)
    def test_untrusted_origins_rejected(self):
        for value in ['http://remote.example','https://user:secret@host','https://host/path','file:///tmp']:
            with self.assertRaises(GatewayError):websocket_endpoint(value)
        self.assertEqual(websocket_endpoint('http://localhost:5159'),'ws://localhost:5159/ws/drone-gateway')
        self.assertEqual(websocket_endpoint('https://api.example.com'),'wss://api.example.com/ws/drone-gateway')
    def test_hardware_uid_is_stable_and_not_system_id(self):
        self.assertEqual(hardware_identity(SimpleNamespace(uid=123456789)), 'UID64-00000000075BCD15')
        self.assertIsNone(hardware_identity(SimpleNamespace(uid=0,uid2=[0]*18)))
        self.assertEqual(hardware_identity(SimpleNamespace(uid=0,uid2=[1]*18)), 'UID2-'+'01'*18)
    def test_valid_uid64_is_consistent_when_uid2_extension_appears(self):
        self.assertEqual(hardware_identity(SimpleNamespace(uid=1)),hardware_identity(SimpleNamespace(uid=1,uid2=[2]*18)))
    def test_identity_handshake_requests_version_from_autopilot(self):
        events=iter([message('AUTOPILOT_VERSION',system=2,uid=222),message('HEARTBEAT',type=2,autopilot=3),message('AUTOPILOT_VERSION',uid=123)])
        requests=[]
        link=SimpleNamespace(recv_match=lambda **kwargs:next(events),mav=SimpleNamespace(command_long_send=lambda *args:requests.append(args)))
        protocol=SimpleNamespace(mavlink=SimpleNamespace(MAV_CMD_REQUEST_MESSAGE=512,MAVLINK_MSG_ID_AUTOPILOT_VERSION=148))
        self.assertEqual(read_identity(link,1,1,protocol),'UID64-000000000000007B')
        self.assertEqual(requests[0][:5],(1,1,512,0,148))
    def test_identity_handshake_has_no_system_id_fallback(self):
        link=SimpleNamespace(recv_match=lambda **kwargs:None)
        with self.assertRaises(GatewayError):read_identity(link,1,1,None,timeout=.001)
    def test_session_is_saved_privately(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'session.json';save_session(path,SESSION)
            self.assertTrue(path.exists())
            if os.name == 'posix':
                self.assertEqual(path.stat().st_mode & 0o777,0o600)

    def test_pair_only_saves_session_closes_link_and_never_starts_worker(self):
        from pymavlink import mavutil
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder)/'session.json'
            link, connection = Mock(), Mock()
            credential = dict(SESSION, connectionId='c', token='t', hardwareIdentity='UID64-0000000000000001')
            argv = ['gateway.py', '--server', 'https://api.example.com', '--device', 'udpin:127.0.0.1:14551',
                    '--system-id', '1', '--session-file', str(path), '--pair-only', '--new-pairing']
            with patch('sys.argv', argv), patch.object(mavutil, 'mavlink_connection', return_value=link), \
                 patch('gateway.read_identity', return_value=credential['hardwareIdentity']), \
                 patch('gateway.getpass.getpass', return_value='ABCDEF0123456789'), \
                 patch('gateway.open_gateway_socket', return_value=(connection, credential)), \
                 patch('gateway.threading.Thread') as worker:
                main()
            self.assertEqual(json.loads(path.read_text())['connectionId'], 'c')
            link.close.assert_called_once()
            connection.close.assert_called_once()
            worker.assert_not_called()

    def test_network_reconnect_retries_inflight_sample_with_same_key(self):
        pending = queue.Queue()
        pending.put(('same-key', {'latitude': 11.35}, 100))
        stop = threading.Event()
        failures, attempts = [], []
        def exchange_fake(connection, message, expected):
            if message['type'] == 'telemetry':
                attempts.append(message)
                if len(attempts) == 1:
                    raise OSError('network lost before ACK')
                stop.set()
            return {'type': expected}
        with patch('gateway.open_gateway_socket', side_effect=lambda *a, **k: (Mock(), None)), \
             patch('gateway.exchange', side_effect=exchange_fake), \
             patch('gateway.time.monotonic', return_value=100), patch.object(stop, 'wait'):
            forward('wss://api.example.com/ws/drone-gateway', SESSION, pending, stop, failures)
        self.assertEqual(len(attempts), 2)
        self.assertEqual(attempts[0], attempts[1])
        self.assertEqual(failures, [])

    def test_service_without_session_waits_without_prompting_or_opening_serial(self):
        with tempfile.TemporaryDirectory() as folder:
            argv = ['gateway.py', '--server', 'https://api.example.com', '--device', 'udpin:127.0.0.1:14551',
                    '--system-id', '1', '--session-file', str(Path(folder)/'absent.json'), '--service']
            with patch('sys.argv', argv), patch('gateway.time.sleep', side_effect=InterruptedError), \
                 patch('gateway.getpass.getpass') as prompt, patch('gateway.read_identity') as identity:
                with self.assertRaises(InterruptedError):
                    main()
                prompt.assert_not_called()
                identity.assert_not_called()

if __name__=='__main__':unittest.main()
