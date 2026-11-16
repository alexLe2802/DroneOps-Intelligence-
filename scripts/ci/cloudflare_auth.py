"""Read-only Cloudflare deployment preflight. Never log credentials or API bodies."""
import json
import os
import re
import sys
import urllib.error
import urllib.request
from pathlib import Path

API = 'https://api.cloudflare.com/client/v4'


class PreflightError(Exception):
    pass


def request(path, token):
    req = urllib.request.Request(API + path, headers={
        'Authorization': 'Bearer ' + token,
        'Accept': 'application/json',
    })
    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            status, raw = response.status, response.read()
    except urllib.error.HTTPError as error:
        status, raw = error.code, error.read()
    except (OSError, TimeoutError):
        raise PreflightError('Cannot reach Cloudflare API. Retry after checking network availability.') from None
    try:
        body = json.loads(raw)
    except (ValueError, UnicodeError):
        raise PreflightError('Cloudflare returned a non-JSON response; check service availability.') from None
    if not isinstance(body, dict):
        raise PreflightError('Cloudflare returned an unexpected response shape.')
    return status, body


def succeeded(result):
    status, body = result
    return status == 200 and body.get('success') is True


def check(token, account, worker, call=request):
    if not token:
        raise PreflightError('Missing repository Actions secret CLOUDFLARE_API_TOKEN.')
    if any(c.isspace() for c in token) or token.startswith(('Bearer', '"', "'")):
        raise PreflightError('CLOUDFLARE_API_TOKEN must contain only the raw token: no quotes, Bearer prefix, spaces or newlines.')
    # User-profile and account-owned API tokens use different verification URLs.
    verification = call('/user/tokens/verify', token)
    if not succeeded(verification):
        verification = call(f'/accounts/{account}/tokens/verify', token)
    if not succeeded(verification):
        raise PreflightError('Cloudflare rejected token verification. Replace the GitHub secret with the actual token value, check expiry/IP restrictions and rerun. Do not paste the token into logs or chat.')
    result = verification[1].get('result')
    if not isinstance(result, dict) or result.get('status') != 'active':
        raise PreflightError('Cloudflare token is not active. Create an active token and replace the GitHub secret.')
    print('Cloudflare token verification: active (credential hidden).')
    access = call(f'/accounts/{account}/workers/services/{worker}', token)
    if not succeeded(access):
        raise PreflightError(f'Token is active but cannot read Worker {worker} in account {account} (HTTP {access[0]}). Check Account resources and Workers Scripts/Edit. DNS/Edit alone cannot deploy Workers. Read access does not prove deployment write permissions.')
    print(f'Cloudflare Worker target access: {worker}, account {account}.')
    print('Read-only preflight passed. Deployment still requires Workers write permissions and Workers Routes/Edit for the custom-domain zone.')


def main():
    config = (Path(__file__).resolve().parents[2] / 'fe/cloudflare.config.ts').read_text()
    account = re.search(r'accountId:\s*"([a-f0-9]{32})"', config)
    worker = re.search(r'name:\s*"([a-zA-Z0-9_-]+)"', config)
    if not account or not worker:
        raise PreflightError('Cannot determine the Worker/account from fe/cloudflare.config.ts. Review the deployment target.')
    check(os.environ.get('CLOUDFLARE_API_TOKEN', ''), account[1], worker[1])


if __name__ == '__main__':
    try:
        main()
    except PreflightError as error:
        print(f'::error::{error}', file=sys.stderr)
        sys.exit(1)
