import unittest
from types import SimpleNamespace

from arducopter47_adapter import ProfileError, apply_to_sitl, parameter_targets, canonical_mission_item


class MissionCanonicalizationTests(unittest.TestCase):
    def item(self, command=16, **changes):
        return dict(dict(seq=2, command=command, frame=3, param1=0,
            param2=2 if command == 16 else 0, param3=0, param4=None,
            x=108231000, y=1066297000, z=20.15), **changes)

    def test_rtl_has_no_stored_destination_or_parameters(self):
        source = self.item(20, frame=2, x=0, y=0, z=0, param4=0)
        result = canonical_mission_item(source, {})
        self.assertEqual(result, source)
        self.assertEqual(source['command'], 20)

    def test_native_nav_placeholders_and_centimeter_altitude(self):
        for command, yaw in ((16, 0), (22, 0), (21, 1)):
            source = self.item(command)
            result = canonical_mission_item(source, {'WP_RADIUS_M': 2})
            self.assertEqual(result['param4'], yaw)
            self.assertEqual(result['param2'], 0)
            self.assertAlmostEqual(result['z'], 20.15, places=5)
            for field in ('seq', 'command', 'frame', 'x', 'y'):
                self.assertEqual(result[field], source[field])
            self.assertIsNone(source['param4'])

    def test_requested_yaw_radius_and_fractional_hold_are_not_silently_lost(self):
        for override in ({'param4': 90}, {'param4': 0}, {'param2': 3}, {'param1': .5}, {'param3': 1}):
            with self.assertRaises(ProfileError):
                canonical_mission_item(self.item(**override), {'WP_RADIUS_M': 2})
        with self.assertRaises(ProfileError):
            canonical_mission_item(self.item(), {})
        self.assertEqual(canonical_mission_item(self.item(param1=5), {'WP_RADIUS_M': 2})['param1'], 5)

    def test_normalized_readback_still_rejects_coordinate_altitude_and_frame_corruption(self):
        from flight_control import same_item
        expected = canonical_mission_item(self.item(), {'WP_RADIUS_M': 2})
        self.assertTrue(same_item(expected, SimpleNamespace(**expected)))
        for override in ({'x': 1}, {'z': 1}, {'frame': 0}, {'param4': 90}, {'param2': 3}):
            self.assertFalse(same_item(expected, SimpleNamespace(**dict(expected, **override))))


class ParameterAdapterTests(unittest.TestCase):
    def setUp(self):
        self.envelope = {'fcParameterVerificationRequired': True, 'configureRecovery': True,
            'cruiseSpeedMetersPerSecond': 3, 'maxSpeedMetersPerSecond': 5,
            'limits': {'policyVersion': 'multicopter-envelope-v1',
                'takeoffSpeedMetersPerSecond': 1, 'climbSpeedMetersPerSecond': 1,
                'descentSpeedMetersPerSecond': .7, 'landingSpeedMetersPerSecond': .7,
                'maxAltitudeMeters': 30,
                'recoveryEnergyProfile': {'returnAltitudeMeters': 20}}}
        self.current = {'FENCE_TYPE': 2, 'FS_OPTIONS': 16, 'BATT_MONITOR': 4,
            'BATT_CAPACITY': 8000, 'BATT_LOW_VOLT': 12.4, 'BATT_CRT_VOLT': 12,
            'BATT_LOW_MAH': 0, 'BATT_CRT_MAH': 0,
            'BATT_FS_LOW_ACT': 2, 'BATT_FS_CRT_ACT': 1}

    def test_basic_waypoint_flight_preserves_all_recovery_parameters(self):
        self.envelope.pop('configureRecovery')
        self.envelope['limits'].pop('recoveryEnergyProfile')
        targets = parameter_targets(self.envelope, {})
        self.assertEqual(targets, {'WP_SPD': 5, 'WP_SPD_UP': 1,
                                  'WP_SPD_DN': .7, 'LAND_SPD_MS': .7})

    def test_native_47_targets_bound_to_policy_and_preserve_unrelated_fence_bits(self):
        result = parameter_targets(self.envelope, self.current)
        self.assertEqual(result['WP_SPD'], 5)
        self.assertEqual(result['WP_SPD_UP'], 1)
        self.assertEqual(result['LAND_SPD_MS'], .7)
        self.assertEqual(result['FENCE_TYPE'], 3)
        self.assertEqual(result['FENCE_ALT_MAX'], 30)
        self.assertEqual(result['RTL_ALT_M'], 20)
        self.assertEqual(result['FS_OPTIONS'], 0)
        self.assertNotIn('BATT_LOW_VOLT', result)  # Never guess battery chemistry.

    def test_mismatched_takeoff_speed_and_missing_battery_threshold_block_before_write(self):
        self.envelope['limits']['takeoffSpeedMetersPerSecond'] = 1.2
        self.envelope['limits']['climbSpeedMetersPerSecond'] = 1.3
        with self.assertRaisesRegex(ProfileError, 'share WP_SPD_UP'):
            parameter_targets(self.envelope, self.current)
        self.envelope['limits']['takeoffSpeedMetersPerSecond'] = 1
        self.envelope['limits']['climbSpeedMetersPerSecond'] = 1
        self.current['BATT_LOW_VOLT'] = 0
        with self.assertRaisesRegex(ProfileError, 'Low battery threshold'):
            parameter_targets(self.envelope, self.current)

    def test_requires_explicit_policy_and_recovery_height(self):
        self.envelope['fcParameterVerificationRequired'] = False
        with self.assertRaises(ProfileError):
            parameter_targets(self.envelope, self.current)

        self.envelope['fcParameterVerificationRequired'] = True
        self.envelope['limits']['recoveryEnergyProfile']['returnAltitudeMeters'] = 40
        with self.assertRaises(ProfileError):
            parameter_targets(self.envelope, self.current)

    def test_v2_rates_map_to_native_units_without_changing_v1_caps(self):
        self.envelope.update(cruiseSpeedMetersPerSecond=11.2, maxSpeedMetersPerSecond=15)
        self.envelope['limits'].update(policyVersion='multicopter-envelope-v2', takeoffSpeedMetersPerSecond=6.5,
            climbSpeedMetersPerSecond=6.5, descentSpeedMetersPerSecond=5.8, landingSpeedMetersPerSecond=1)
        targets = parameter_targets(self.envelope, self.current)
        self.assertEqual(targets['WP_SPD'], 15)
        self.assertEqual(targets['WP_SPD_UP'], 6.5)
        self.assertEqual(targets['WP_SPD_DN'], 5.8)
        self.assertEqual(targets['LAND_SPD_MS'], 1)
        self.envelope['limits']['policyVersion'] = 'multicopter-envelope-v1'
        with self.assertRaises(ProfileError):
            parameter_targets(self.envelope, self.current)

    def test_unreviewed_battery_actions_and_inverted_thresholds_rejected(self):
        for changes in ({'BATT_FS_LOW_ACT': 1}, {'BATT_FS_CRT_ACT': 2},
                        {'BATT_CRT_VOLT': 13}, {'BATT_LOW_MAH': -1},
                        {'BATT_LOW_MAH': 100, 'BATT_CRT_MAH': 200},
                        {'BATT_LOW_MAH': 9000}):
            with self.subTest(changes=changes), self.assertRaises(ProfileError):
                parameter_targets(self.envelope, dict(self.current, **changes))

    def test_ack_then_independent_readback_required(self):
        class Link:
            def __init__(self, match=True):
                self.mav = self
                self.match = match
                self.name = None
                self.read = False
                self.set_count = 0
            def param_set_send(self, system, component, name, value, kind):
                self.name = name.decode()
                self.value = value
                self.set_count += 1
            def param_request_read_send(self, system, component, name, index):
                self.read = True
            def recv_match(self, **kwargs):
                if self.read:
                    self.read = False
                    return SimpleNamespace(get_srcSystem=lambda: 1, get_srcComponent=lambda: 1,
                        param_id=self.name, param_value=self.value if self.match else self.value + 1)
                return SimpleNamespace(get_srcSystem=lambda: 1, get_srcComponent=lambda: 1,
                    param_id=self.name, param_value=self.value)
        link = Link()
        self.assertTrue(apply_to_sitl(link, {'WP_SPD': 3}))
        self.assertEqual(link.set_count, 1)
        with self.assertRaisesRegex(ProfileError, 'read-back mismatch'):
            apply_to_sitl(Link(False), {'WP_SPD': 3})


if __name__ == '__main__':
    unittest.main()
