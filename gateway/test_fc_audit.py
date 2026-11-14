import unittest
from collections import deque
from types import SimpleNamespace
from unittest.mock import Mock
from fc_audit import audit


def message(kind, system=1, component=1, **values):
    return SimpleNamespace(get_type=lambda: kind, get_srcSystem=lambda: system,
                           get_srcComponent=lambda: component,
                           to_dict=lambda: dict(values), **values)


class AuditTests(unittest.TestCase):
    def run_audit(self, messages, duration=1):
        now = [0.0]
        messages = deque(messages)
        link = SimpleNamespace(mav=Mock(), recv_match=lambda **kw: messages.popleft() if messages else None)
        def sleep(seconds):
            now[0] += seconds
        return audit(link, duration=duration, clock=lambda: now[0], sleep=sleep), link

    def test_no_fc_is_explicit_and_sends_nothing(self):
        report, link = self.run_audit([])
        self.assertFalse(report['heartbeatReceived'])
        self.assertFalse(report['flightAuthorized'])
        self.assertEqual(link.mav.mock_calls, [])

    def test_read_only_bound_source_and_privacy(self):
        report, link = self.run_audit([
            message('HEARTBEAT', autopilot=3, type=2, base_mode=0),
            message('AUTOPILOT_VERSION', flight_sw_version=(4 << 24) | (7 << 16) | (1 << 8) | 255, uid=123, uid2=[1]),
            message('PARAM_VALUE', system=2, param_id='FRAME_CLASS', param_value=99),
            message('PARAM_VALUE', param_id=b'FRAME_CLASS\0', param_value=1),
            message('GPS_RAW_INT', lat=123, lon=456, fix_type=1),
        ])
        self.assertEqual(report['firmwareVersion'], '4.7.1')
        self.assertEqual(report['parameters']['FRAME_CLASS'], 1)
        self.assertNotIn('uid', report['messages']['AUTOPILOT_VERSION'])
        self.assertNotIn('lat', report['messages']['GPS_RAW_INT'])
        self.assertTrue(link.mav.param_request_read_send.called)
        for call in link.mav.command_long_send.call_args_list:
            self.assertEqual(call.args[2], 512)
        link.mav.heartbeat_send.assert_not_called()
        link.mav.param_set_send.assert_not_called()
        link.mav.mission_count_send.assert_not_called()
        self.assertFalse(report['flightAuthorized'])
        self.assertFalse(report['recoveryPreflight']['flightAuthorized'])
        self.assertIn('No 3D GPS fix from FC.', report['recoveryPreflight']['blockers'])

    def test_unrequested_and_nonfinite_parameters_are_discarded(self):
        report, _ = self.run_audit([
            message('HEARTBEAT', autopilot=12, type=2, base_mode=0),
            message('PARAM_VALUE', param_id='MPC_XY_CRUISE', param_value=float('nan')),
            message('PARAM_VALUE', param_id='NOT_REQUESTED', param_value=100),
        ])
        self.assertEqual(report['parameters'], {})
        self.assertIn('MPC_XY_CRUISE', report['missingParameters'])
        self.assertEqual(report['firmware'], 'PX4')


if __name__ == '__main__':
    unittest.main()
