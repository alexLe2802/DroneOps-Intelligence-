#!/usr/bin/env bash
set -euo pipefail

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
sudo -u droneops /opt/droneops-gateway/.venv/bin/python \
  /opt/droneops-gateway/gateway.py \
  --server "$DRONEOPS_SERVER" \
  --device "$MAVLINK_DEVICE" \
  --baud "$MAVLINK_BAUD" \
  --system-id "$MAVLINK_SYSTEM_ID" \
  --component-id "$MAVLINK_COMPONENT_ID" \
  --session-file /var/lib/droneops-gateway/session.private.json \
  --new-pairing --pair-only "${extra[@]}"

systemctl enable --now droneops-gateway
echo "Gateway paired and enabled for future boots."
systemctl --no-pager --full status droneops-gateway

