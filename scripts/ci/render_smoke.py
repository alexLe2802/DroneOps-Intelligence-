"""Validate the Render Blueprint and run its exact image against disposable DB."""
import json
import os
import secrets
import subprocess
import time
import urllib.error
import urllib.request
from pathlib import Path

import jsonschema
import yaml

if os.environ.get('DRONEOPS_CI_ISOLATED') != 'true':
    raise SystemExit('Disposable CI isolation is required.')
# Official schema validation is read-only and requires no Render account secrets.
with urllib.request.urlopen('https://render.com/schema/render.yaml.json', timeout=30) as response:
    schema = json.load(response)
config = yaml.safe_load(Path('render.yaml').read_text())
jsonschema.validate(config, schema)
service = next(s for s in config['services'] if s['name'] == 'droneops-api-demo')
if service['runtime'] != 'docker' or service['healthCheckPath'] != '/health':
    raise SystemExit('Review the CI smoke contract for the changed Render service.')
subprocess.run(['docker', 'build', '--file', service['dockerfilePath'], '--tag', 'droneops-ci-api', service['dockerContext']], check=True)
connection = os.environ['DRONEOPS_CI_POSTGRES']
if not (connection.startswith('Host=127.0.0.1;') and os.environ.get('DRONEOPS_CI_DATABASE', 'droneops_ci_app').startswith('droneops_ci_')):
    raise SystemExit('Render CI rejects shared database targets.')
connection = connection.replace('Database=postgres;', f'Database={os.environ.get("DRONEOPS_CI_DATABASE", "droneops_ci_app")};')
ca = Path(os.environ['DRONEOPS_CI_CA']).resolve(strict=True)
connection += ';SSL Mode=VerifyFull;Root Certificate=/run/ci/postgres.crt'
env = dict(os.environ, ConnectionStrings__DefaultConnection=connection,
           ASPNETCORE_ENVIRONMENT='Production', ASPNETCORE_HTTP_PORTS='18080', PORT='18080',
           Monitoring__Enabled='false', Monitoring__DedicatedProcessRequired='true', FlightExecution__Enabled='false',
           Jwt__Key=secrets.token_urlsafe(48), Jwt__Issuer='droneops-ci', Jwt__Audience='droneops-ci',
           IntegrationSettings__ApiKey=secrets.token_urlsafe(32))
keys = ['ConnectionStrings__DefaultConnection', 'ASPNETCORE_ENVIRONMENT', 'ASPNETCORE_HTTP_PORTS', 'PORT',
        'Monitoring__Enabled', 'Monitoring__DedicatedProcessRequired', 'FlightExecution__Enabled',
        'Jwt__Key', 'Jwt__Issuer', 'Jwt__Audience', 'IntegrationSettings__ApiKey']
command = ['docker', 'run', '--detach', '--rm', '--network', 'host', '--name', 'droneops-ci-api',
           '--mount', f'type=bind,src={ca},dst=/run/ci/postgres.crt,readonly']
for key in keys:
    command.extend(['--env', key])  # Values remain out of shell/command output.
try:
    subprocess.run(command + ['droneops-ci-api'], env=env, check=True, stdout=subprocess.DEVNULL)
    for attempt in range(60):
        try:
            with urllib.request.urlopen('http://127.0.0.1:18080/health', timeout=3) as response:
                if response.status == 200:
                    break
        except (OSError, TimeoutError):
            time.sleep(1)
    else:
        raise RuntimeError('Render image failed /health in 60 seconds. Inspect startup/schema configuration.')
    # Operational data must never become public in a deployable image.
    try:
        urllib.request.urlopen('http://127.0.0.1:18080/api/missions', timeout=5)
        raise RuntimeError('Unauthenticated mission access was accepted.')
    except urllib.error.HTTPError as error:
        if error.code not in (401, 403):
            raise
    print('PASS: Render Blueprint, production Docker image, /health and authorization boundary.')
finally:
    log = Path(os.environ.get('RUNNER_TEMP', '/tmp')) / 'droneops-render-ci.log'
    with log.open('w') as output:
        subprocess.run(['docker', 'logs', 'droneops-ci-api'], stdout=output, stderr=subprocess.STDOUT)
    if os.environ.get('DRONEOPS_CI_KEEP_API') != 'true':
        subprocess.run(['docker', 'rm', '--force', 'droneops-ci-api'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
