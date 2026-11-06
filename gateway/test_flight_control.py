import queue
import time
import datetime as dt
import unittest
from types import SimpleNamespace
from unittest.mock import Mock

from flight_control import ControlBridge, FlightControl, same_item, planned_home_matches
import test_arducopter47_adapter as adapter_fixtures


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

    def test_basic_route_upload_readback_arm_and_start_without_recovery_setup(self):
        self.feed(autopilot=3, mode=5)
        self.control.accept(message('AUTOPILOT_VERSION', capabilities=4, flight_sw_version=0x04070100), self.link)
        self.control.accept(message('PARAM_VALUE', param_id='ARMING_SKIPCHK', param_value=0), self.link)
        fixture = adapter_fixtures.ParameterAdapterTests()
        fixture.setUp()
        envelope = dict(fixture.envelope, configureRecovery=False)
        self.response(requiredEnvelopeHash='hash', operatingEnvelope=envelope, stage='AwaitingFcProfile')
        while self.control.envelope.pending:
            name = self.control.envelope.pending[0]
            for _ in range(2):
                self.control.accept(message('PARAM_VALUE', param_id=name,
                    param_value=self.control.envelope.targets[name]), self.link)
        self.assertEqual(self.control.envelope.verified_hash, 'hash')
        self.assertEqual(set(self.control.envelope.targets), {'WP_SPD', 'WP_SPD_UP', 'WP_SPD_DN', 'LAND_SPD_MS'})
        def item(seq, command, x, z):
            return dict(seq=seq, command=command, frame=3, param1=0, param2=0,
                        param3=0, param4=None, x=x, y=1070000000, z=z)
        plan = dict(firmwareTarget='ArduPilot', operationalEnvelope=envelope,
                    items=[item(0, 22, 106000000, 20), item(1, 16, 106001000, 20), item(2, 21, 106001000, 0)])
        self.response('Upload', identifier='upload', mission=plan, stage='Uploading', requiredEnvelopeHash='hash')
        self.link.mav.mission_count_send.assert_called_once_with(1, 1, 4, 0)
        for seq in range(4):
            self.control.accept(message('MISSION_REQUEST_INT', seq=seq), self.link)
        self.control.accept(message('MISSION_ACK', type=0), self.link)
        self.control.accept(message('MISSION_COUNT', count=4), self.link)
        for entry in self.control.items:
            values = dict(entry)
            if values['param4'] is None:
                values['param4'] = float('nan')
            self.control.accept(message('MISSION_ITEM_INT', **values), self.link)
        self.assertTrue(self.bridge.report['succeeded'])
        self.assertEqual(self.control.uploaded_hash, 'hash')
        self.response('Arm', identifier='arm', stage='Arming', requiredEnvelopeHash='hash')
        self.control.accept(message('COMMAND_ACK', command=400, result=0), self.link)
        self.feed(armed=True, autopilot=3, mode=5)
        self.assertTrue(self.bridge.report['succeeded'])
        self.now += 10
        self.feed(armed=True, autopilot=3, mode=5)
        self.control.accept(message('AUTOPILOT_VERSION', capabilities=4, flight_sw_version=0x04070100), self.link)
        self.response('Start', identifier='start', stage='Starting', requiredEnvelopeHash='hash')
        self.control.accept(message('COMMAND_ACK', command=300, result=0), self.link)
        self.feed(armed=True, autopilot=3, mode=3)
        self.assertTrue(self.bridge.report['succeeded'])
        self.assertEqual(self.link.mav.command_long_send.call_count, 2)

    def test_map_takeoff_must_match_actual_home_even_without_explicit_home(self):
        self.feed(autopilot=3)
        item = dict(seq=0, command=22, frame=3, param1=0, param2=0, param3=0,
                    param4=None, x=110000000, y=1070000000, z=20)
        self.response('Upload', mission=dict(firmwareTarget='ArduPilot',
            operationalEnvelope={'configureRecovery': False}, items=[item]), stage='Uploading')
        self.link.mav.mission_count_send.assert_not_called()
        self.assertFalse(self.bridge.report['succeeded'])

    def test_default_is_telemetry_only(self):
        self.control.enabled = False
        self.response('Arm')
        self.link.mav.command_long_send.assert_not_called()
        self.assertFalse(self.bridge.report['succeeded'])

    def test_planned_home_requires_fresh_matching_fc_coordinates(self):
        actual = SimpleNamespace(latitude=108231000, longitude=1066297000, altitude=12000)
        self.assertTrue(planned_home_matches({'latitude': 10.8231, 'longitude': 106.6297}, actual))
        self.assertTrue(planned_home_matches({'latitude': 10.8231, 'longitude': 106.6297, 'altitudeMslMeters': 12}, actual))
        for expected in ({'latitude': 11, 'longitude': 106.6297},
                         {'latitude': 10.8231, 'longitude': 106.6297, 'altitudeMslMeters': 30},
                         {'latitude': float('nan'), 'longitude': 106.6297}):
            self.assertFalse(planned_home_matches(expected, actual))
        self.assertFalse(planned_home_matches({'latitude': 10.8231, 'longitude': 106.6297}, None))

    def test_upload_checks_planned_home_but_keeps_actual_fc_home_header(self):
        self.feed(autopilot=3)
        item = dict(seq=0, command=22, frame=3, param1=0, param2=0, param3=0, param4=None, x=106000000, y=1070000000, z=20)
        self.response('Upload', mission=dict(firmwareTarget='ArduPilot', items=[item],
            plannedHome=dict(latitude=10.600001, longitude=107.0)), stage='Uploading')
        self.link.mav.mission_count_send.assert_called_once()
        self.assertEqual(self.control.items[0]['x'], 106000000)
        self.assertEqual(self.control.items[0]['z'], 12)
        self.link.mav.command_long_send.assert_not_called()

    def test_upload_rejects_distant_planned_home_without_writing_mission(self):
        self.feed(autopilot=3)
        self.response('Upload', mission=dict(firmwareTarget='ArduPilot', items=[],
            plannedHome=dict(latitude=11, longitude=107)), stage='Uploading')
        self.link.mav.mission_count_send.assert_not_called()
        self.assertFalse(self.bridge.report['succeeded'])

    def test_arm_requires_current_local_envelope_receipt_when_backend_requires_it(self):
        self.response('Arm', stage='Arming', requiredEnvelopeHash='hash')
        self.link.mav.command_long_send.assert_not_called()
        self.assertFalse(self.bridge.report['succeeded'])

    def test_approved_envelope_is_applied_nonblocking_and_receipt_bound_to_hash(self):
        fixture = adapter_fixtures.ParameterAdapterTests()
        fixture.setUp()
        self.feed(autopilot=3)
        self.control.accept(message('AUTOPILOT_VERSION', capabilities=4, flight_sw_version=0x04070100), self.link)
        for name, value in dict(fixture.current, ARMING_SKIPCHK=0, FS_EKF_ACTION=1).items():
            self.control.accept(message('PARAM_VALUE', param_id=name, param_value=value), self.link)
        self.response(requiredEnvelopeHash='hash', operatingEnvelope=fixture.envelope, stage='AwaitingFcProfile')
        while self.control.envelope.pending:
            name = self.control.envelope.pending[0]
            value = self.control.envelope.targets[name]
            self.control.accept(message('PARAM_VALUE', param_id=name, param_value=value), self.link)
            self.assertIsNone(self.control.evidence()['verifiedEnvelopeHash'])
            self.control.accept(message('PARAM_VALUE', param_id=name, param_value=value), self.link)
        self.assertEqual(self.control.evidence()['verifiedEnvelopeHash'], 'hash')
        self.bridge.reconnect()
        self.control.tick(self.link)
        self.assertIsNone(self.control.evidence()['verifiedEnvelopeHash'])

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

    def test_arducopter_47_native_units_are_preserved(self):
        self.feed(autopilot=3)
        self.control.tick(self.link)
        names = [call.args[2] for call in self.link.mav.param_request_read_send.call_args_list]
        self.assertIn(b'WP_RADIUS_M', names)
        self.assertIn(b'LAND_SPD_MS', names)
        self.assertIn(b'BATT_CAPACITY', names)
        self.control.accept(message('AUTOPILOT_VERSION', capabilities=4, flight_sw_version=(4 << 24) | (7 << 16) | (1 << 8)), self.link)
        for name, value in [('WP_RADIUS_M', 2), ('WP_SPD', 10), ('LAND_SPD_MS', .5)]:
            self.control.accept(message('PARAM_VALUE', param_id=name, param_value=value), self.link)
        snapshot = self.control.evidence()['firmwareParameters']
        self.assertEqual(snapshot['firmwareVersion'], '4.7.1')
        self.assertEqual(snapshot['values']['WP_RADIUS_M'], 2)
        self.assertEqual(snapshot['values']['LAND_SPD_MS'], .5)
        self.link.mav.param_set_send.assert_not_called()

    def test_battery_capacity_is_automatically_read_for_both_firmwares(self):
        for autopilot, parameter in ((3, 'BATT_CAPACITY'), (12, 'BAT1_CAPACITY')):
            self.control.parameters_requested = float('-inf')
            self.feed(autopilot=autopilot)
            self.control.tick(self.link)
            self.assertIn(parameter.encode(), [call.args[2] for call in self.link.mav.param_request_read_send.call_args_list])
            self.control.accept(message('AUTOPILOT_VERSION', capabilities=4, flight_sw_version=0x04070100), self.link)
            self.control.accept(message('PARAM_VALUE', param_id=parameter, param_value=8000), self.link)
            self.assertEqual(self.control.evidence()['firmwareParameters']['values'][parameter], 8000)
        self.link.mav.param_set_send.assert_not_called()

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
