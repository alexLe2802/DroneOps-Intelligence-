#!/usr/bin/env bash
set -euo pipefail

if [[ $EUID -ne 0 ]]; then
  echo "Run this installer with sudo: sudo ./install-jetson.sh" >&2
  exit 1
fi

source_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
install_dir=/opt/droneops-gateway
state_dir=/var/lib/droneops-gateway
env_file=/etc/droneops-gateway.env
service_file=/etc/systemd/system/droneops-gateway.service

for file in gateway.py requirements.txt droneops-gateway.service.example pair-jetson.sh; do
  if [[ ! -f "$source_dir/$file" ]]; then
    echo "Missing $file in $source_dir. Run this script from the extracted gateway package." >&2
    exit 1
  fi
done

if ! command -v python3 >/dev/null || ! python3 -m venv --help >/dev/null 2>&1; then
  echo "Install prerequisites first: sudo apt update && sudo apt install -y python3-venv python3-pip" >&2
  exit 1
fi

echo "Detected serial devices:"
find /dev/serial/by-id -maxdepth 1 -type l -print 2>/dev/null || true
compgen -G '/dev/ttyACM*' || true
compgen -G '/dev/ttyUSB*' || true
echo
read -r -p "Pixhawk device (prefer /dev/serial/by-id/...): " mavlink_device
if [[ "$mavlink_device" != /dev/* || ! -e "$mavlink_device" ]]; then
  echo "Device does not exist: $mavlink_device" >&2
  exit 1
fi

read -r -p "MAVLink baud [921600]: " mavlink_baud
mavlink_baud=${mavlink_baud:-921600}
[[ "$mavlink_baud" =~ ^[1-9][0-9]*$ ]] || { echo "Baud must be a positive number." >&2; exit 1; }

read -r -p "MAVLink system ID [1]: " system_id
system_id=${system_id:-1}
[[ "$system_id" =~ ^[0-9]+$ ]] && (( system_id >= 1 && system_id <= 255 )) || {
  echo "System ID must be between 1 and 255." >&2; exit 1;
}

read -r -p "Backend origin (example http://192.168.1.20:5159): " server
if [[ ! "$server" =~ ^https://[^/]+$ && ! "$server" =~ ^http://[^/]+$ ]]; then
  echo "Use an origin without a path, for example http://192.168.1.20:5159 or https://api.example.com" >&2
  exit 1
fi

if ! id droneops >/dev/null 2>&1; then
  useradd --system --home "$state_dir" --create-home --shell /usr/sbin/nologin droneops
fi
usermod -aG dialout droneops
install -d -o droneops -g droneops "$install_dir" "$state_dir"
install -m 0644 "$source_dir/gateway.py" "$source_dir/requirements.txt" "$install_dir/"
install -m 0755 "$source_dir/pair-jetson.sh" "$install_dir/"
install -m 0644 "$source_dir/droneops-gateway.service.example" "$service_file"
chown -R droneops:droneops "$install_dir" "$state_dir"

if [[ ! -x "$install_dir/.venv/bin/python" ]]; then
  sudo -u droneops python3 -m venv "$install_dir/.venv"
fi
sudo -u droneops "$install_dir/.venv/bin/pip" install --upgrade pip
sudo -u droneops "$install_dir/.venv/bin/pip" install -r "$install_dir/requirements.txt"

umask 077
printf '%s\n' \
  "DRONEOPS_SERVER=$server" \
  "MAVLINK_DEVICE=$mavlink_device" \
  "MAVLINK_BAUD=$mavlink_baud" \
  "MAVLINK_SYSTEM_ID=$system_id" \
  "MAVLINK_COMPONENT_ID=1" > "$env_file"

override_dir=/etc/systemd/system/droneops-gateway.service.d
if [[ "$server" == http://* ]]; then
  install -d -m 0755 "$override_dir"
  cat > "$override_dir/lan-development.conf" <<'EOF'
[Service]
ExecStart=
ExecStart=/opt/droneops-gateway/.venv/bin/python /opt/droneops-gateway/gateway.py --server ${DRONEOPS_SERVER} --device ${MAVLINK_DEVICE} --baud ${MAVLINK_BAUD} --system-id ${MAVLINK_SYSTEM_ID} --component-id ${MAVLINK_COMPONENT_ID} --session-file /var/lib/droneops-gateway/session.private.json --allow-insecure-websocket
EOF
  echo "WARNING: HTTP/WS mode is only suitable for a trusted development LAN."
else
  if [[ -f "$override_dir/lan-development.conf" ]]; then
    rm "$override_dir/lan-development.conf"
  fi
fi

systemctl daemon-reload
systemctl disable --now droneops-gateway >/dev/null 2>&1 || true

echo
echo "Installation complete. Pair once with:"
echo "  sudo $install_dir/pair-jetson.sh"

