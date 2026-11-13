"""Enable verified TLS on the disposable Actions PostgreSQL service only."""
import os
import subprocess
from pathlib import Path

if os.environ.get('DRONEOPS_CI_ISOLATED') != 'true':
    raise SystemExit('Disposable isolation is required.')
container = os.environ['CI_POSTGRES_CONTAINER']
if not container or any(c not in '0123456789abcdef' for c in container):
    raise SystemExit('Expected the Actions service container ID.')
root = Path(os.environ['RUNNER_TEMP']) / 'droneops-ci-tls'
root.mkdir(mode=0o700, exist_ok=True)
cert, key = root / 'postgres.crt', root / 'postgres.key'
subprocess.run(['openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1',
                '-subj', '/CN=localhost', '-addext', 'subjectAltName=DNS:localhost,IP:127.0.0.1',
                '-addext', 'basicConstraints=critical,CA:TRUE', '-keyout', str(key), '-out', str(cert)],
               check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
key.chmod(0o600)
for local, remote in [(cert, '/var/lib/postgresql/ci.crt'), (key, '/var/lib/postgresql/ci.key')]:
    subprocess.run(['docker', 'cp', str(local), f'{container}:{remote}'], check=True)
subprocess.run(['docker', 'exec', '--user', 'root', container, 'chown', 'postgres:postgres',
                '/var/lib/postgresql/ci.crt', '/var/lib/postgresql/ci.key'], check=True)
subprocess.run(['docker', 'exec', '--user', 'root', container, 'chmod', '600', '/var/lib/postgresql/ci.key'], check=True)
for sql in ["ALTER SYSTEM SET ssl_cert_file='/var/lib/postgresql/ci.crt'", "ALTER SYSTEM SET ssl_key_file='/var/lib/postgresql/ci.key'", "ALTER SYSTEM SET ssl=on", 'SELECT pg_reload_conf()']:
    subprocess.run(['docker', 'exec', '--user', 'postgres', container, 'psql', '-U', 'postgres', '-v', 'ON_ERROR_STOP=1', '-c', sql],
                   check=True, stdout=subprocess.DEVNULL)
with Path(os.environ['GITHUB_ENV']).open('a') as out:
    out.write(f'DRONEOPS_CI_CA={cert}\n')
print('Disposable PostgreSQL TLS enabled; the app will still require VerifyFull.')
