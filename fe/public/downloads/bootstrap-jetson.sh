#!/usr/bin/env bash
# Install/update DroneOps on a Jetson and claim one web pairing code.
set -euo pipefail

[[ $EUID -eq 0 ]] || { echo 'Run this bootstrap with sudo.' >&2; exit 1; }
server=''; package_url=''; gcs_host=''; otp=''; baud=115200; system_id=1
while [[ $# -gt 0 ]]; do
  case "$1" in
    --server) server=${2:-}; shift 2 ;;
    --package-url) package_url=${2:-}; shift 2 ;;
    --gcs-host) gcs_host=${2:-}; shift 2 ;;
    --otp) otp=${2:-}; shift 2 ;;
    --baud) baud=${2:-}; shift 2 ;;
    --system-id) system_id=${2:-}; shift 2 ;;
    *) echo "Unknown option: $1" >&2; exit 1 ;;
  esac
done
[[ "$server" =~ ^https?://[a-zA-Z0-9.-]+(:[0-9]+)?$ ]] || { echo 'Invalid backend origin.' >&2; exit 1; }
[[ "$package_url" =~ ^https?://[a-zA-Z0-9.-]+(:[0-9]+)?/downloads/droneops-gateway\.zip$ ]] || { echo 'Invalid gateway package URL.' >&2; exit 1; }
[[ "$gcs_host" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]] || { echo 'Invalid QGroundControl/Mission Planner laptop IPv4.' >&2; exit 1; }
IFS=. read -r -a gcs_octets <<< "$gcs_host"
for octet in "${gcs_octets[@]}"; do (( 10#$octet <= 255 )) || { echo 'Invalid GCS laptop IPv4.' >&2; exit 1; }; done
[[ "$otp" =~ ^[A-Za-z0-9]{8,64}$ ]] || { echo 'Invalid pairing code.' >&2; exit 1; }
[[ "$baud" =~ ^[1-9][0-9]*$ ]] || { echo 'Invalid baud.' >&2; exit 1; }
[[ "$system_id" =~ ^[1-9][0-9]{0,2}$ ]] && (( system_id <= 255 )) || { echo 'Invalid system ID.' >&2; exit 1; }

mapfile -t serial_devices < <(find /dev/serial/by-id -maxdepth 1 -type l -print 2>/dev/null | sort)
if [[ ${#serial_devices[@]} -eq 0 ]]; then
  echo 'No USB serial adapter was found in /dev/serial/by-id.' >&2
  echo 'Connect the Pixhawk adapter and run the copied command again.' >&2
  exit 1
fi
serial_device=${serial_devices[0]}
if [[ ${#serial_devices[@]} -gt 1 ]]; then
  echo 'Available USB serial adapters:'
  for index in "${!serial_devices[@]}"; do
    printf '  %d) %s\n' "$((index + 1))" "${serial_devices[$index]}"
  done
  while true; do
    read -r -p "Select the Pixhawk USB serial adapter [1-${#serial_devices[@]}]: " selection
    if [[ "$selection" =~ ^[1-9][0-9]*$ ]] && (( selection <= ${#serial_devices[@]} )); then
      serial_device=${serial_devices[$((selection - 1))]}
      break
    fi
    echo "Enter a number from 1 to ${#serial_devices[@]}." >&2
  done
fi
echo "Using Pixhawk serial adapter: $serial_device"

work_dir=$(mktemp -d)
trap 'rm -rf -- "$work_dir"' EXIT
apt-get update
apt-get install -y curl unzip
curl --fail --silent --show-error --location "$package_url" --output "$work_dir/gateway.zip"
unzip -q "$work_dir/gateway.zip" -d "$work_dir/package"
install_script="$work_dir/package/droneops-gateway/install-jetson.sh"
[[ -f "$install_script" ]] || { echo 'The downloaded gateway package is incomplete.' >&2; exit 1; }

DRONEOPS_INSTALL_SERIAL=$serial_device \
DRONEOPS_INSTALL_BAUD=$baud \
DRONEOPS_INSTALL_SYSTEM_ID=$system_id \
DRONEOPS_INSTALL_SERVER=$server \
DRONEOPS_INSTALL_HTTP_CONSENT=LAN \
DRONEOPS_INSTALL_NONINTERACTIVE=1 \
DRONEOPS_INSTALL_GCS_IP=$gcs_host \
  bash "$install_script"

/opt/droneops-gateway/pair-jetson.sh --code "$otp"
echo
printf '\033[1;35m▲ DroneOps Gateway\033[0m\n'
printf '  \033[1;32mQGroundControl\033[0m  udp://%s:14550\n' "$gcs_host"
printf '  \033[1;35mMission Planner\033[0m udp://%s:14551\n' "$gcs_host"
printf '  \033[1;36mBackend\033[0m        %s\n' "$server"
printf '  \033[1;32m✓ Ready\033[0m · services start automatically on boot\n'
printf '  Live gateway log follows. Press Ctrl+C to stop viewing; services keep running.\n\n'
journalctl -u droneops-gateway -n 20 -f -o cat
