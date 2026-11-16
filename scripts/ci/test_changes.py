import unittest
import os
import sys
import subprocess
import tempfile
from pathlib import Path
from changes import select, affected_suites


class SelectionChecks(unittest.TestCase):
    def test_document_only_keeps_gate_without_rebuilding(self):
        self.assertFalse(any(select(['docs/Review 2 submission/Report3.docx']).values()))

    def test_database_requires_backend_and_deployment(self):
        result = select(['database/migrations/new.sql'])
        self.assertTrue(result['backend'])
        self.assertFalse(result['frontend'])

    def test_workflow_and_agents_changes_run_all_software_suites(self):
        for path in ['AGENTS.md', '.github/workflows/ci.yml', 'scripts/ci/changes.py']:
            result = select([path])
            self.assertTrue(all(result[k] for k in ['backend', 'frontend', 'gateway']))
            self.assertFalse(result['sitl'])

    def test_telemetry_only_change_does_not_download_the_simulator(self):
        result = select(['gateway/telemetry.py'])
        self.assertTrue(result['gateway'])
        self.assertFalse(result['sitl'])

    def test_gateway_logic_runs_simulator_but_not_backend(self):
        result = select(['gateway/flight_control.py'])
        self.assertTrue(result['sitl'])
        self.assertFalse(result['backend'])


class HistoryChecks(unittest.TestCase):
    def setUp(self):
        self.workspace = tempfile.TemporaryDirectory()
        self.addCleanup(self.workspace.cleanup)
        self.root = Path(self.workspace.name)
        self.git('init', '-q')
        self.git('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.test',
                 'commit', '-q', '--allow-empty', '-m', 'Initial source')
        self.base = self.git('rev-parse', 'HEAD')
        (self.root / 'docs').mkdir()
        (self.root / 'docs' / 'guide.md').write_text('Project guide')
        self.git('add', 'docs/guide.md')
        self.git('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.test',
                 'commit', '-q', '-m', 'Documentation')
        self.head = self.git('rev-parse', 'HEAD')

    def git(self, *args):
        return subprocess.check_output(['git', *args], cwd=self.root).decode().strip()

    def test_available_history_preserves_selective_checks(self):
        self.assertFalse(any(affected_suites(self.base, self.head, cwd=self.root).values()))

    def test_missing_pre_rewrite_commit_runs_every_suite(self):
        self.assertTrue(all(affected_suites('f' * 40, self.head, cwd=self.root).values()))

    def test_new_branch_zero_sha_runs_every_suite(self):
        self.assertTrue(all(affected_suites('0' * 40, self.head, cwd=self.root).values()))

    def test_empty_base_runs_every_suite(self):
        self.assertTrue(all(affected_suites('', self.head, cwd=self.root).values()))

    def test_cli_writes_all_outputs_when_old_sha_is_missing(self):
        output = self.root / 'github-output.txt'
        env = {**os.environ, 'PR_BASE_SHA': 'f' * 40, 'PR_HEAD_SHA': self.head,
               'GITHUB_OUTPUT': str(output)}
        result = subprocess.run([sys.executable, str(Path(__file__).with_name('changes.py').resolve())],
                                cwd=self.root, env=env, text=True, capture_output=True, check=True)
        self.assertIn('::warning::Base commit is unavailable', result.stdout)
        self.assertEqual(dict(line.split('=') for line in output.read_text().splitlines()),
                         dict(backend='true', frontend='true', gateway='true', sitl='true'))

    def test_missing_head_does_not_hide_checkout_failure(self):
        with self.assertRaisesRegex(RuntimeError, 'head commit is unavailable'):
            affected_suites(self.base, 'f' * 40, cwd=self.root)


if __name__ == '__main__':
    unittest.main()
