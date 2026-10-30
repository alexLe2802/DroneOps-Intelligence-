#!/usr/bin/env bash
# One-time setup; services reconnect without a terminal.
set -euo pipefail
[[ $EUID -eq 0 ]] || { echo 'Run: sudo bash install-jetson.sh'; exit 1; }
source_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
install_dir=/opt/droneops-gateway
state_dir=/var/lib/droneops-gateway
for file in gateway.py requirements.txt droneops-gateway.service.example droneops-router.service.example pair-jetson.sh; do
  [[ -f "$source_dir/$file" ]] || { echo "Missing $file"; exit 1; }
done
echo 'Pixhawk serial adapters:'
find /dev/serial/by-id -maxdepth 1 -type l -print 2>/dev/null || true
serial_device=${DRONEOPS_INSTALL_SERIAL:-}
[[ -n "$serial_device" ]] || read -r -p 'Pixhawk serial path (/dev/serial/by-id/...): ' serial_device
[[ "$serial_device" =~ ^/dev/[a-zA-Z0-9_./:+-]+$ && -e "$serial_device" ]] || { echo 'Use an existing /dev serial device without spaces.'; exit 1; }
baud=${DRONEOPS_INSTALL_BAUD:-}
[[ -n "$baud" ]] || read -r -p 'Baud matching Pixhawk TELEM2 [115200]: ' baud
baud=${baud:-115200}
[[ "$baud" =~ ^[1-9][0-9]*$ ]] || { echo 'Invalid baud'; exit 1; }
system_id=${DRONEOPS_INSTALL_SYSTEM_ID:-}
[[ -n "$system_id" ]] || read -r -p 'MAVLink system ID [1]: ' system_id
system_id=${system_id:-1}
[[ "$system_id" =~ ^[1-9][0-9]{0,2}$ ]] && (( system_id <= 255 )) || { echo 'Invalid system ID'; exit 1; }
server=${DRONEOPS_INSTALL_SERVER:-}
[[ -n "$server" ]] || read -r -p 'Backend origin (https://api.example.com): ' server
[[ "$server" =~ ^https?://[a-zA-Z0-9.-]+(:[0-9]+)?$ ]] || { echo 'Use an HTTP(S) origin without path, credentials or spaces.'; exit 1; }
transport_flag=''
if [[ "$server" == http://* ]]; then
  consent=${DRONEOPS_INSTALL_HTTP_CONSENT:-}
  [[ -n "$consent" ]] || read -r -p 'Unencrypted LAN development only. Type LAN to continue: ' consent
  [[ "$consent" == LAN ]] || exit 1
  transport_flag=--allow-insecure-websocket
fi
laptop=${DRONEOPS_INSTALL_GCS_IP:-}
if [[ -z "$laptop" && "${DRONEOPS_INSTALL_NONINTERACTIVE:-}" != 1 ]]; then
  read -r -p 'Optional laptop IPv4 for QGroundControl (blank = web only): ' laptop
fi
if [[ -n "$laptop" ]]; then
  [[ "$laptop" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]] || { echo 'Use a laptop IPv4 address'; exit 1; }
  IFS=. read -r -a octets <<< "$laptop"
  for octet in "${octets[@]}"; do (( 10#$octet <= 255 )) || { echo 'Invalid IPv4'; exit 1; }; done
fi
systemctl stop droneops-gateway droneops-router 2>/dev/null || true
apt-get update
apt-get install -y python3-venv python3-pip git meson ninja-build pkg-config gcc g++ libsystemd-dev
if ! command -v mavlink-routerd >/dev/null; then
  if ! apt-get install -y mavlink-router; then
    build_dir=$(mktemp -d)
    git clone --recurse-submodules https://github.com/mavlink-router/mavlink-router.git "$build_dir/router"
    meson setup "$build_dir/router/build" "$build_dir/router"
    ninja -C "$build_dir/router/build"
    ninja -C "$build_dir/router/build" install
    echo "Router source retained at $build_dir/router; record the commit for reproducibility."
  fi
fi
router_binary=$(command -v mavlink-routerd)
id droneops >/dev/null 2>&1 || useradd --system --home "$state_dir" --create-home --shell /usr/sbin/nologin droneops
usermod -aG dialout droneops
install -d -o droneops -g droneops "$install_dir" "$state_dir"
install -m 0644 "$source_dir/gateway.py" "$source_dir/requirements.txt" "$install_dir/"
install -m 0755 "$source_dir/pair-jetson.sh" "$install_dir/"
[[ -x "$install_dir/.venv/bin/python" ]] || python3 -m venv "$install_dir/.venv"
"$install_dir/.venv/bin/pip" install -r "$install_dir/requirements.txt"
chown -R root:root "$install_dir"
chown droneops:droneops "$state_dir"
umask 077
printf '%s\n' "DRONEOPS_SERVER=$server" 'MAVLINK_DEVICE=udpin:127.0.0.1:14551' \
  "MAVLINK_BAUD=$baud" "MAVLINK_SYSTEM_ID=$system_id" 'MAVLINK_COMPONENT_ID=1' \
  "DRONEOPS_TRANSPORT_FLAG=$transport_flag" > /etc/droneops-gateway.env
chown root:droneops /etc/droneops-gateway.env
chmod 0640 /etc/droneops-gateway.env
install -d -m 0755 /etc/droneops-router
cat > /etc/droneops-router/main.conf <<EOF
[General]
TcpServerPort=0
[UartEndpoint pixhawk]
Device=$serial_device
Baud=$baud
FlowControl=false
[UdpEndpoint gateway]
Mode=Normal
Address=127.0.0.1
Port=14551
EOF
if [[ -n "$laptop" ]]; then
  cat >> /etc/droneops-router/main.conf <<EOF
[UdpEndpoint qgroundcontrol]
Mode=Normal
Address=$laptop
Port=14550
[UdpEndpoint missionplanner]
Mode=Normal
Address=$laptop
Port=14551
EOF
fi
chmod 0644 /etc/droneops-router/main.conf
install -m 0644 "$source_dir/droneops-gateway.service.example" /etc/systemd/system/droneops-gateway.service
sed "s|/usr/local/bin/mavlink-routerd|$router_binary|" "$source_dir/droneops-router.service.example" > /etc/systemd/system/droneops-router.service
legacy=/etc/systemd/system/droneops-gateway.service.d/lan-development.conf
[[ ! -f "$legacy" ]] || rm -- "$legacy"
systemctl daemon-reload
systemctl enable --now droneops-router droneops-gateway
echo 'Ready. Services start on boot and retry when serial/network return.'
echo 'Create a code on Connect Drone, then run: sudo /opt/droneops-gateway/pair-jetson.sh'
