import unittest

from arducopter_recovery import REQUIRED, assess


class RecoveryPreflightTests(unittest.TestCase):
    def setUp(self):
        self.parameters = dict.fromkeys(REQUIRED, 1)
        self.parameters.update(ARMING_CHECK=1, FS_THR_ENABLE=1, FS_GCS_ENABLE=1,
            FS_GCS_TIMEOUT=5, FS_OPTIONS=0, FS_EKF_ACTION=1,
            FENCE_ENABLE=1, FENCE_TYPE=1, FENCE_ACTION=1, FENCE_ALT_MAX=30,
            BATT_MONITOR=4, BATT_CAPACITY=8000, BATT_LOW_VOLT=12.4,
            BATT_CRT_VOLT=12, BATT_LOW_MAH=0, BATT_CRT_MAH=0,
            BATT_FS_LOW_ACT=2, BATT_FS_CRT_ACT=1)
        self.messages = {'BATTERY_STATUS': {'voltages': [4000, 4000, 4000, 65535]},
                         'GPS_RAW_INT': {'fix_type': 3}, 'HOME_POSITION': {}}

    def test_even_complete_readonly_audit_never_authorizes_flight(self):
        result = assess(self.parameters, self.messages)
        self.assertTrue(result['parameterAuditPassed'])
        self.assertFalse(result['flightAuthorized'])
        self.assertTrue(result['remainingVerification'])

    def test_observed_usb_only_fc_reports_actual_blockers(self):
        self.parameters.update(WP_SPD=10, FENCE_ENABLE=0, FS_GCS_ENABLE=0,
                               FS_OPTIONS=16)
        self.messages = {'BATTERY_STATUS': {'voltages': [3, 65535]},
                         'GPS_RAW_INT': {'fix_type': 1}}
        blockers = ' '.join(assess(self.parameters, self.messages)['blockers'])
        for term in ('geofence', 'GCS-loss', 'voltage', 'GPS', 'Home'):
            self.assertIn(term, blockers)

    def test_missing_or_nonfinite_parameters_fail_closed(self):
        self.parameters.pop('FS_THR_ENABLE')
        self.parameters['BATT_CRT_VOLT'] = float('nan')
        blockers = ' '.join(assess(self.parameters, self.messages)['blockers'])
        self.assertIn('FS_THR_ENABLE', blockers)
        self.assertIn('BATT_CRT_VOLT', blockers)

    def test_continuing_auto_after_loss_is_not_accepted(self):
        self.parameters['FS_OPTIONS'] = 3
        self.assertIn('continuing AUTO', ' '.join(assess(self.parameters, self.messages)['blockers']))
        self.parameters['FS_OPTIONS'] = 16
        self.assertIn('pilot control', ' '.join(assess(self.parameters, self.messages)['blockers']))

    def test_low_and_critical_battery_require_thresholds_and_order(self):
        self.parameters.update(BATT_LOW_VOLT=0, BATT_CRT_VOLT=0)
        blockers = ' '.join(assess(self.parameters, self.messages)['blockers'])
        self.assertIn('Low battery has no', blockers)
        self.assertIn('Critical battery has no', blockers)
        self.parameters.update(BATT_LOW_VOLT=12, BATT_CRT_VOLT=12.4)
        self.assertIn('Critical voltage must be lower', ' '.join(assess(self.parameters, self.messages)['blockers']))


if __name__ == '__main__':
    unittest.main()
