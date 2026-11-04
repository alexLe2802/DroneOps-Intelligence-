import queue
import time
import datetime as dt
import unittest
from types import SimpleNamespace
from unittest.mock import Mock

from flight_control import ControlBridge, FlightControl, same_item


def message(kind, **values):
    return SimpleNamespace(get_type=lambda: kind, get_srcSystem=lambda: 1, get_srcComponent=lambda: 1, **values)


class FlightControlTests(unittest.TestCase):
    def setUp(self):
        self.now = 100.0
        self.bridge = ControlBridge()
        self.link = SimpleNamespace(mav=Mock(), source_system=255, source_component=0)
        self.control = FlightControl(1, 1, self.bridge, True, lambda: self.now)
        self.control.tick(self.link)
        self.feed()
        self.control.uploaded_hash = 'hash'

    def feed(self, armed=False, landed=1, autopilot=12, mode=3 << 16):
        for kind, fields in (
            ('HEARTBEAT', dict(type=2, autopilot=autopilot, base_mode=128 if armed else 0, custom_mode=mode)),
            ('SYS_STATUS', dict(onboard_control_sensors_enabled=1 << 28, onboard_control_sensors_health=1 << 28)),
            ('EXTENDED_SYS_STATE', dict(landed_state=landed)),
            ('GPS_RAW_INT', dict(fix_type=3)),
            ('HOME_POSITION', dict(latitude=106000000, longitude=1070000000, altitude=12000)),
            ('AUTOPILOT_VERSION', dict(capabilities=4)),
        ):
            self.control.accept(message(kind, **fields), self.link)

    def response(self, action=None, identifier='id', **extra):
        utc = dt.datetime.now(dt.timezone.utc)
        value = dict(enabled=True, leaseSeconds=3, stage='Countdown', compiledHash='hash',
                     serverNow=utc.isoformat(), command=dict(id=identifier, action=action,
                         expiresAt=(utc + dt.timedelta(seconds=60 if action == 'Upload' else 8)).isoformat()) if action else None)
        value.update(extra)
        self.bridge.responses.put((self.bridge.instance, self.now, value))
        self.control.tick(self.link)

    def test_default_is_telemetry_only(self):
        self.control.enabled = False
        self.response('Arm')
        self.link.mav.command_long_send.assert_not_called()
        self.assertFalse(self.bridge.report['succeeded'])

    def test_backend_disable_does_not_emit_gcs_heartbeat_or_commands(self):
        self.response('Arm', enabled=False)
        self.link.mav.command_long_send.assert_not_called()
        self.link.mav.heartbeat_send.assert_not_called()

    def test_stalled_mavlink_reader_cannot_refresh_old_evidence_as_live(self):
        self.control.tick(self.link)
        self.bridge.evidence_at = time.monotonic() - 4
        value = self.bridge.payload()['evidence']
        self.assertIsNone(value['armed'])
        self.assertIsNone(value['landedState'])
        self.assertFalse(value['navigationReady'])

    def test_fc_parameters_are_read_only_and_source_bound(self):
        self.control.tick(self.link)
        self.link.mav.param_request_read_send.assert_called()
        self.link.mav.param_set_send.assert_not_called()
        self.control.accept(message('AUTOPILOT_VERSION', capabilities=4, flight_sw_version=(1 << 24) | (17 << 16)), self.link)
        self.control.accept(message('PARAM_VALUE', param_id=b'MPC_XY_CRUISE\x00', param_value=3.0), self.link)
        snapshot = self.control.evidence()['firmwareParameters']
        self.assertEqual(snapshot['firmwareVersion'], '1.17.0')
        self.assertEqual(snapshot['values']['MPC_XY_CRUISE'], 3)
        wrong = message('PARAM_VALUE', param_id='NAV_ACC_RAD', param_value=100)
        wrong.get_srcSystem = lambda: 42
        self.control.accept(wrong, self.link)
        self.assertNotIn('NAV_ACC_RAD', self.control.evidence()['firmwareParameters']['values'])

    def test_nonfinite_or_stale_fc_parameters_are_not_defaults(self):
        self.control.accept(message('PARAM_VALUE', param_id='NAV_ACC_RAD', param_value=float('nan')), self.link)
        self.assertIsNone(self.control.evidence()['firmwareParameters'])
        self.control.accept(message('PARAM_VALUE', param_id='NAV_ACC_RAD', param_value=2), self.link)
        self.now += 61
        self.feed()
        self.control.accept(message('AUTOPILOT_VERSION', capabilities=4, flight_sw_version=(1 << 24) | (17 << 16)), self.link)
        self.assertIsNone(self.control.evidence()['firmwareParameters'])

    def test_arm_is_not_retried_or_counted_down_on_ack_alone(self):
        self.response('Arm', stage='Arming')
        self.assertEqual(self.link.mav.command_long_send.call_args.args, (1, 1, 400, 0, 1, 0, 0, 0, 0, 0, 0))
        self.control.accept(message('COMMAND_ACK', command=400, result=0), self.link)
        self.assertIsNone(self.control.arm_at)
        self.control.accept(message('HEARTBEAT', type=2, autopilot=12, base_mode=128, custom_mode=3 << 16), self.link)
        self.assertEqual(self.control.arm_at, self.now)
        self.response('Arm', stage='Arming')
        self.assertEqual(self.link.mav.command_long_send.call_count, 1)

    def test_rejected_arm_never_starts_countdown(self):
        self.response('Arm', stage='Arming')
        self.control.accept(message('COMMAND_ACK', command=400, result=2), self.link)
        self.assertFalse(self.bridge.report['succeeded'])
        self.assertIsNone(self.control.arm_at)

    def test_auto_mode_cannot_arm_before_countdown(self):
        for autopilot, mode in ((3, 3), (12, (4 << 16) | (4 << 24))):
            self.setUp()
            self.feed(autopilot=autopilot, mode=mode)
            self.response('Arm', stage='Arming')
            self.link.mav.command_long_send.assert_not_called()

    def test_unknown_or_airborne_ground_state_blocks_disarm(self):
        for ground in (0, 2, 3, 4):
            self.setUp()
            self.feed(armed=True, landed=ground)
            self.response('Disarm')
            self.link.mav.command_long_send.assert_not_called()

    def test_ground_disarm_never_sets_force_parameter(self):
        self.feed(armed=True)
        self.response('Disarm')
        self.assertEqual(self.link.mav.command_long_send.call_args.args, (1, 1, 400, 0, 0, 0, 0, 0, 0, 0, 0))

    def test_rc_arm_cannot_start_even_with_server_command(self):
        self.feed(armed=True)
        self.response('Start', stage='Starting')
        self.link.mav.command_long_send.assert_not_called()

    def test_local_ten_second_delay_cannot_be_shortened_by_backend(self):
        self.feed(armed=True)
        self.control.arm_at = self.now - 9.9
        self.response('Start', stage='Starting')
        self.link.mav.command_long_send.assert_not_called()

    def test_start_consumes_permission_and_is_sent_once(self):
        self.feed(armed=True)
        self.control.arm_at = self.now - 10
        self.response('Start', stage='Starting')
        self.response('Start', stage='Starting')
        self.assertEqual(self.link.mav.command_long_send.call_count, 1)
        self.assertEqual(self.link.mav.command_long_send.call_args.args[2], 300)
        self.assertIsNone(self.control.arm_at)

    def test_lease_expiry_cancels_and_reconnect_never_resumes(self):
        self.feed(armed=True)
        self.control.arm_at = self.now - 10
        self.control.lease = self.now - 1
        self.control.tick(self.link)
        self.assertIsNone(self.control.arm_at)
        self.response('Start', stage='Starting')
        self.link.mav.command_long_send.assert_not_called()

    def test_new_socket_instance_invalidates_verified_upload(self):
        self.control.arm_at = self.now
        self.bridge.reconnect()
        self.control.tick(self.link)
        self.assertIsNone(self.control.uploaded_hash)
        self.assertIsNone(self.control.arm_at)

    def test_stale_socket_response_cannot_arm(self):
        previous = self.bridge.instance
        self.bridge.reconnect()
        self.bridge.responses.put((previous, self.now, dict(enabled=True, leaseSeconds=3,
            command=dict(id='old', action='Arm'), compiledHash='hash')))
        self.control.tick(self.link)
        self.link.mav.command_long_send.assert_not_called()

    def test_wrong_source_and_wrong_target_ack_ignored(self):
        self.response('Arm', stage='Arming')
        ack = message('COMMAND_ACK', command=400, result=0, target_system=42)
        self.control.accept(ack, self.link)
        self.assertFalse(self.control.ack)
        ack.target_system = 255
        ack.get_srcSystem = lambda: 42
        self.control.accept(ack, self.link)
        self.assertFalse(self.control.ack)

    def test_upload_requires_ack_and_exact_readback(self):
        item = dict(seq=0, command=22, frame=3, param1=0, param2=0, param3=0, param4=None,
                    x=106000000, y=1070000000, z=20)
        self.response('Upload', mission=dict(firmwareTarget='PX4', items=[item]), stage='Uploading')
        self.assertIsNone(self.control.uploaded_hash)
        self.control.accept(message('MISSION_REQUEST_INT', seq=0), self.link)
        self.control.accept(message('MISSION_ACK', type=0), self.link)
        self.assertIsNone(self.control.uploaded_hash)
        self.control.accept(message('MISSION_COUNT', count=1), self.link)
        self.control.accept(message('MISSION_ITEM_INT', **dict(item, param4=float('nan'), autocontinue=1)), self.link)
        self.assertEqual(self.control.uploaded_hash, 'hash')
        self.assertTrue(self.bridge.report['succeeded'])

    def test_upload_cannot_succeed_from_early_ack(self):
        item = dict(seq=0, command=22, frame=3, param1=0, param2=0, param3=0, param4=None, x=1, y=2, z=20)
        self.response('Upload', mission=dict(firmwareTarget='PX4', items=[item]), stage='Uploading')
        self.control.accept(message('MISSION_ACK', type=0), self.link)
        self.assertFalse(self.bridge.report['succeeded'])

    def test_operational_policy_is_not_mistaken_for_verified_fc_parameters(self):
        self.response('Upload', mission=dict(firmwareTarget='PX4', items=[],
            operationalEnvelope=dict(fcParameterVerificationRequired=True)), stage='Uploading')
        self.link.mav.mission_count_send.assert_not_called()
        self.link.mav.command_long_send.assert_not_called()
        self.assertFalse(self.bridge.report['succeeded'])

    def test_readback_mismatch_and_nonfinite_values_rejected(self):
        item = dict(seq=0, command=16, frame=3, param1=1, param2=2, param3=0, param4=None, x=1, y=2, z=20)
        read = dict(item, param4=float('nan'))
        self.assertTrue(same_item(item, SimpleNamespace(**read)))
        for field, bad in (('x', 3), ('frame', 0), ('z', float('nan')), ('param2', 7), ('param4', 0)):
            self.assertFalse(same_item(item, SimpleNamespace(**dict(read, **{field: bad}))))

    def test_ardupilot_home_prefix_and_sequence(self):
        self.feed(autopilot=3, mode=5)
        item = dict(seq=0, command=22, frame=3, param1=0, param2=0, param3=0, param4=None, x=1, y=2, z=20)
        self.response('Upload', mission=dict(firmwareTarget='ArduPilot', items=[item]), stage='Uploading')
        self.assertEqual([i['seq'] for i in self.control.items], [0, 1])
        self.assertEqual(self.control.items[0]['z'], 12)
        self.assertEqual(self.control.items[1]['command'], 22)

    def test_command_timeout_has_no_automatic_retry(self):
        self.response('Arm', stage='Arming')
        self.now += 9
        self.feed()
        self.control.tick(self.link)
        self.assertIsNone(self.control.active)
        self.assertFalse(self.bridge.report['succeeded'])
        self.assertEqual(self.link.mav.command_long_send.call_count, 1)

    def test_delayed_backend_command_is_not_executed(self):
        self.response('Arm', serverNow=(dt.datetime.now(dt.timezone.utc) - dt.timedelta(seconds=4)).isoformat())
        self.link.mav.command_long_send.assert_not_called()


if __name__ == '__main__':
    unittest.main()
