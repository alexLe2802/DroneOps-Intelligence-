import unittest
from changes import select


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


if __name__ == '__main__':
    unittest.main()
