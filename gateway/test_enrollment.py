import json
import queue
import threading
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from gateway import GatewayError, GatewayWaiting, open_gateway_socket, forward
from connect import choose_port, detect_autopilot


class Socket:
    def __init__(self, response):
        self.response = response
        self.sent = None
        self.closed = False

    def send(self, value): self.sent = json.loads(value)
    def recv(self): return json.dumps(self.response)
    def close(self): self.closed = True


class EnrollmentTests(unittest.TestCase):
    def test_reboot_uses_device_key_and_preserves_it_while_switching_mission_lease(self):
        session = dict(uavId='uav', hardwareIdentity='UID64-0000000000000001', deviceToken='private-device-key', connectionId='old', token='expired')
        socket = Socket({'type': 'session', 'payload': dict(uavId='uav', hardwareIdentity=session['hardwareIdentity'],
                        connectionId='new', missionId='new-mission', token='new-lease', deviceToken=None)})
        with patch.dict('sys.modules', {'websocket': SimpleNamespace(create_connection=lambda *a, **k: socket)}):
            open_gateway_socket('wss://api.example.test/ws/drone-gateway', session=session)
        self.assertEqual(socket.sent['type'], 'deviceResume')
        self.assertNotIn('token', socket.sent['payload'])
        self.assertEqual(session['deviceToken'], 'private-device-key')
        self.assertEqual(session['connectionId'], 'new')
        self.assertEqual(session['missionId'], 'new-mission')

    def test_trusted_gateway_rejects_a_different_physical_aircraft(self):
        session = dict(uavId='uav', hardwareIdentity='UID64-0000000000000001', deviceToken='private')
        socket = Socket({'type': 'session', 'payload': dict(uavId='other', hardwareIdentity=session['hardwareIdentity'])})
        with patch.dict('sys.modules', {'websocket': SimpleNamespace(create_connection=lambda *a, **k: socket)}):
            with self.assertRaisesRegex(GatewayError, 'different UAV'):
                open_gateway_socket('wss://api.example.test/ws/drone-gateway', session=session)
        self.assertTrue(socket.closed)

    def test_waiting_for_web_mission_is_distinct_from_revoked_credential(self):
        session = dict(uavId='uav', hardwareIdentity='UID64-0000000000000001', deviceToken='private')
        socket = Socket({'type': 'waiting', 'message': 'Select a mission on the web.'})
        with patch.dict('sys.modules', {'websocket': SimpleNamespace(create_connection=lambda *a, **k: socket)}):
            with self.assertRaises(GatewayWaiting):
                open_gateway_socket('wss://api.example.test/ws/drone-gateway', session=session)
        self.assertTrue(socket.closed)

    def test_mission_switch_discards_a_sample_captured_for_the_old_mission(self):
        session = dict(connectionId='old', missionId='old', sourceId='old', deviceToken='private')
        pending = queue.Queue()
        pending.put(('old-key', {'missionId': 'old'}, 100))
        stop = threading.Event()
        sent = []
        def resume(*args, **kwargs):
            session.update(connectionId='new', missionId='new', sourceId='new')
            return Socket({}), None
        def exchange(connection, message, expected):
            sent.append(message)
            stop.set()
            return {'type': expected}
        with patch('gateway.open_gateway_socket', side_effect=resume), patch('gateway.exchange', side_effect=exchange), patch('gateway.time.monotonic', return_value=100):
            forward('wss://fixture.invalid/ws/drone-gateway', session, pending, stop, [])
        self.assertEqual([message['type'] for message in sent], ['heartbeat'])
        self.assertTrue(pending.empty())

    def test_one_usb_radio_needs_no_port_or_ip_input(self):
        self.assertEqual(choose_port([SimpleNamespace(device='COM9')], lambda _: self.fail('No prompt expected')), 'COM9')

    def test_radio_discovery_skips_gcs_heartbeat_before_flight_controller(self):
        messages = iter([SimpleNamespace(autopilot=8), SimpleNamespace(autopilot=12,
            get_srcComponent=lambda: 1, get_srcSystem=lambda: 42)])
        closed = []
        link = SimpleNamespace(recv_match=lambda **kwargs: next(messages), close=lambda: closed.append(True))
        mavutil = SimpleNamespace(mavlink_connection=lambda *args, **kwargs: link,
            mavlink=SimpleNamespace(MAV_AUTOPILOT_ARDUPILOTMEGA=3, MAV_AUTOPILOT_PX4=12, MAV_COMP_ID_AUTOPILOT1=1))
        with patch('gateway.read_identity', return_value='UID64-0000000000000001'):
            result = detect_autopilot('COM9', mavutil, bauds=(57600,))
        self.assertEqual(result, (57600, 42, 1, 'UID64-0000000000000001'))
        self.assertEqual(closed, [True])

    def test_multiple_usb_devices_require_an_explicit_choice(self):
        ports = [SimpleNamespace(device='COM3', description='Other USB device'), SimpleNamespace(device='COM9', description='Ground radio')]
        answers = iter(['0', 'invalid', '2'])
        self.assertEqual(choose_port(ports, lambda _: next(answers)), 'COM9')


if __name__ == '__main__': unittest.main()
