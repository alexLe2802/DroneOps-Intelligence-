"""Select affected suites without suppressing the required PR check."""
import os
import subprocess
from pathlib import Path


def select(paths):
    shared = any(p.startswith(('.github/', 'scripts/ci/')) or p in {'AGENTS.md', 'global.json'} for p in paths)
    backend = shared or any(p.startswith(('be/', 'database/')) or p in {'Dockerfile', '.dockerignore', 'render.yaml'} for p in paths)
    frontend = shared or any(p.startswith('fe/') for p in paths)
    gateway = shared or any(p.startswith('gateway/') for p in paths)
    flight_runtime = {
        'gateway/arducopter47_adapter.py', 'gateway/sitl_arducopter47.py',
        'gateway/envelope_verifier.py', 'gateway/flight_control.py',
        'gateway/requirements.txt',
    }
    return dict(backend=backend, frontend=frontend, gateway=gateway,
                sitl=any(p in flight_runtime for p in paths))


def affected_suites(base, head, *, cwd=None):
    def exists(revision):
        return subprocess.run(
            ['git', 'rev-parse', '--verify', '--end-of-options', f'{revision}^{{commit}}'],
            cwd=cwd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
        ).returncode == 0

    if not exists(head):
        raise RuntimeError('CI head commit is unavailable; checkout must contain PR_HEAD_SHA.')
    # Force-push/history cleanup (or a new branch's zero SHA) can remove the
    # event's previous commit from the checkout. Do not infer an empty diff.
    if not base or not exists(base):
        print('::warning::Base commit is unavailable after history change; running all suites.')
        return dict(backend=True, frontend=True, gateway=True, sitl=True)
    changed = subprocess.check_output(
        ['git', 'diff', '--name-only', '-z', base, head], cwd=cwd,
    ).decode().split('\0')
    return select([p for p in changed if p])


if __name__ == '__main__':
    base = os.environ['PR_BASE_SHA']
    head = os.environ['PR_HEAD_SHA']
    result = affected_suites(base, head)
    with Path(os.environ['GITHUB_OUTPUT']).open('a') as out:
        for key, value in result.items():
            out.write(f'{key}={str(value).lower()}\n')
    print('Affected suites:', ', '.join(k for k, v in result.items() if v) or 'metadata only')
