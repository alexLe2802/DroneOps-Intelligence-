#!/usr/bin/env bash
set -euo pipefail

pair_code=''
if [[ "${1:-}" == '--code' ]]; then
  pair_code=${2:-}
  [[ "$pair_code" =~ ^[A-Za-z0-9]{8,64}$ ]] || { echo 'Invalid pairing code.' >&2; exit 1; }
  shift 2
fi
[[ $# -eq 0 ]] || { echo 'Usage: pair-jetson.sh [--code PAIRING_CODE]' >&2; exit 1; }

if [[ $EUID -ne 0 ]]; then
  echo "Run with sudo: sudo /opt/droneops-gateway/pair-jetson.sh" >&2
  exit 1
fi

env_file=/etc/droneops-gateway.env
if [[ ! -r "$env_file" ]]; then
  echo "Missing $env_file. Run install-jetson.sh first." >&2
  exit 1
fi

set -a
# This root-owned file is created by install-jetson.sh.
source "$env_file"
set +a

extra=()
if [[ "$DRONEOPS_SERVER" == http://* ]]; then
  extra+=(--allow-insecure-websocket)
fi

systemctl stop droneops-gateway >/dev/null 2>&1 || true
trap 'systemctl start droneops-gateway' EXIT
pair_gateway() {
  sudo -u droneops /opt/droneops-gateway/.venv/bin/python \
    /opt/droneops-gateway/gateway.py \
    --server "$DRONEOPS_SERVER" \
    --device "$MAVLINK_DEVICE" \
    --baud "$MAVLINK_BAUD" \
    --system-id "$MAVLINK_SYSTEM_ID" \
    --component-id "$MAVLINK_COMPONENT_ID" \
    --session-file /var/lib/droneops-gateway/session.private.json \
    --new-pairing --pair-only "$@" "${extra[@]}"
}
if [[ -n "$pair_code" ]]; then
  printf '%s\n' "$pair_code" | pair_gateway --pair-code-stdin
else
  pair_gateway
fi

systemctl enable --now droneops-gateway
echo "Gateway paired and enabled for future boots."
systemctl --no-pager --full status droneops-gateway

