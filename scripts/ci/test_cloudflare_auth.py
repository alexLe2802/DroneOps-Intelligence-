import contextlib
import io
import unittest
from unittest.mock import Mock

from cloudflare_auth import PreflightError, check

ACTIVE = (200, {'success': True, 'result': {'status': 'active'}})
ACCESS = (200, {'success': True, 'result': {}})
DENIED = (403, {'success': False})
FAKE_TOKEN = 'synthetic-test-credential'


class CloudflareAuthChecks(unittest.TestCase):
    def test_invalid_format_never_calls_api(self):
        for token in ['', 'Bearer example', 'example\n', '"example"']:
            with self.subTest(token=repr(token)):
                call = Mock()
                with self.assertRaises(PreflightError):
                    check(token, 'account', 'worker', call)
                call.assert_not_called()

    def test_invalid_token_has_actionable_error_without_leak(self):
        call = Mock(side_effect=[DENIED, DENIED])
        with self.assertRaisesRegex(PreflightError, 'Replace the GitHub secret') as caught:
            check(FAKE_TOKEN, 'account', 'worker', call)
        self.assertNotIn(FAKE_TOKEN, str(caught.exception))

    def test_inactive_token_blocks_before_worker_access(self):
        call = Mock(return_value=(200, {'success': True, 'result': {'status': 'disabled'}}))
        with self.assertRaisesRegex(PreflightError, 'not active'):
            check(FAKE_TOKEN, 'account', 'worker', call)
        self.assertEqual(call.call_count, 1)

    def test_valid_token_with_wrong_scope_is_distinct(self):
        call = Mock(side_effect=[ACTIVE, DENIED])
        with contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaisesRegex(PreflightError, 'active but cannot read Worker'):
                check(FAKE_TOKEN, 'account', 'worker', call)

    def test_user_token_success_does_not_leak_or_claim_write_access(self):
        call = Mock(side_effect=[ACTIVE, ACCESS])
        with contextlib.redirect_stdout(io.StringIO()) as output:
            check(FAKE_TOKEN, 'account', 'worker', call)
        self.assertNotIn(FAKE_TOKEN, output.getvalue())
        self.assertIn('Deployment still requires', output.getvalue())
        self.assertEqual(call.call_args_list[-1].args[0], '/accounts/account/workers/services/worker')

    def test_account_token_verifies_with_account_endpoint(self):
        call = Mock(side_effect=[DENIED, ACTIVE, ACCESS])
        with contextlib.redirect_stdout(io.StringIO()):
            check(FAKE_TOKEN, 'account', 'worker', call)
        self.assertEqual(call.call_args_list[1].args[0], '/accounts/account/tokens/verify')


if __name__ == '__main__':
    unittest.main()
