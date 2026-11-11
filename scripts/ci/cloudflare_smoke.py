"""Smoke test the built Worker in local workerd, never a hosted deployment."""
import os
import subprocess
import signal
import time
import urllib.request
import urllib.error
from pathlib import Path

if os.environ.get('DRONEOPS_CI_ISOLATED') != 'true':
    raise SystemExit('Local CI isolation flag is required.')
env = dict(os.environ, DRONEOPS_CI='true')
log = Path(os.environ.get('RUNNER_TEMP', '/tmp')) / 'droneops-cloudflare-ci.log'
with log.open('w') as output:
    process = subprocess.Popen(['npm', 'run', 'start:vinext', '--', '--host', '127.0.0.1', '--port', '4173'],
                               cwd='fe', env=env, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
    try:
        for attempt in range(60):
            if process.poll() is not None:
                raise RuntimeError(f'Worker runtime exited; inspect {log.name}')
            try:
                with urllib.request.urlopen('http://127.0.0.1:4173/login', timeout=3) as response:
                    if response.status == 200 and b'<html' in response.read(200_000).lower():
                        print('Cloudflare local Worker: /login returns HTML (200).')
                        break
            except (OSError, TimeoutError):
                pass
            time.sleep(1)
        else:
            raise RuntimeError(f'Worker login smoke test timed out; inspect {log.name}')
        # When backend code changed too, prove Worker -> exact Render image ->
        # authorization. FE-only PRs verify the local session endpoint offline.
        path = '/api/missions' if os.environ.get('DRONEOPS_CI_BACKEND_READY') == 'true' else '/api/auth/me'
        try:
            urllib.request.urlopen('http://127.0.0.1:4173' + path, timeout=5)
            raise RuntimeError('Worker accepted unauthenticated access.')
        except urllib.error.HTTPError as error:
            if error.code not in (401, 403):
                raise
        print(f'Cloudflare local Worker: unauthenticated {path} is denied.')
    finally:
        try:
            os.killpg(process.pid, signal.SIGTERM)
        except ProcessLookupError:
            pass
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGKILL)
            process.wait()
