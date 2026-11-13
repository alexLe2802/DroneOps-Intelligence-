import unittest
from types import SimpleNamespace
from unittest.mock import Mock

from envelope_verifier import EnvelopeVerifier
import test_arducopter47_adapter as fixtures


class EnvelopeVerifierTests(unittest.TestCase):
    def setUp(self):
        fixture = fixtures.ParameterAdapterTests()
        fixture.setUp()
        self.policy, self.current = fixture.envelope, fixture.current
        self.now = 1
        self.link = SimpleNamespace(mav=Mock())
        self.verifier = EnvelopeVerifier(1, 1, lambda: self.now)

    def packet(self, name, value, system=1):
        return SimpleNamespace(get_type=lambda: 'PARAM_VALUE', get_srcSystem=lambda: system,
                               get_srcComponent=lambda: 1, param_id=name, param_value=value)

    def complete(self):
        while self.verifier.pending:
            name = self.verifier.pending[0]
            packet = self.packet(name, self.verifier.targets[name])
            self.verifier.accept(packet, self.link)
            self.assertIsNone(self.verifier.verified_hash)
            self.verifier.accept(packet, self.link)

    def test_receipt_requires_all_ack_and_readback_then_drift_revokes(self):
        self.verifier.begin('mission-a', self.policy, self.current, self.link)
        self.complete()
        self.assertEqual(self.verifier.verified_hash, 'mission-a')
        self.assertEqual(self.link.mav.param_set_send.call_count, 13)
        self.verifier.accept(self.packet('WP_SPD', 20, system=99), self.link)
        self.assertEqual(self.verifier.verified_hash, 'mission-a')
        self.verifier.accept(self.packet('WP_SPD', 20), self.link)
        self.assertIsNone(self.verifier.verified_hash)
        self.assertEqual(self.verifier.failed_hash, 'mission-a')

    def test_timeout_does_not_retry_or_authorize(self):
        self.verifier.begin('mission-a', self.policy, self.current, self.link)
        self.now += 4
        self.verifier.tick()
        self.assertIsNone(self.verifier.verified_hash)
        self.assertIn('timed out', self.verifier.error)
        self.verifier.begin('mission-a', self.policy, self.current, self.link)
        self.assertEqual(self.link.mav.param_set_send.call_count, 1)

    def test_readback_expiry_revokes_receipt(self):
        self.verifier.begin('mission-a', self.policy, self.current, self.link)
        self.complete()
        self.now += 61
        self.verifier.tick()
        self.assertIsNone(self.verifier.verified_hash)

    def test_changed_battery_configuration_revokes_receipt(self):
        self.verifier.begin('mission-a', self.policy, self.current, self.link)
        self.complete()
        self.verifier.accept(self.packet('BATT_CAPACITY', 10000), self.link)
        self.assertIsNone(self.verifier.verified_hash)
        self.assertIn('configuration changed', self.verifier.error)

    def test_missing_configuration_is_not_invented(self):
        self.verifier.begin('mission-a', self.policy, {}, self.link)
        self.link.mav.param_set_send.assert_not_called()
        self.assertIsNone(self.verifier.verified_hash)

    def test_native_rtl_uses_existing_fc_height_without_energy_measurements(self):
        self.policy['limits'].pop('recoveryEnergyProfile')
        self.verifier.begin('mission-a', self.policy, dict(self.current, RTL_ALT_M=15), self.link)
        self.assertEqual(self.verifier.targets['RTL_ALT_M'], 15)


if __name__ == '__main__':
    unittest.main()
