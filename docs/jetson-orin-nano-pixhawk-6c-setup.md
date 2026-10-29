# Jetson Orin Nano + Pixhawk 6C trên drone

Hướng dẫn này áp dụng cho **Jetson Orin Nano Developer Kit**, **Pixhawk 6C chạy
PX4** và gateway đang có trên branch `Qthong`. Nếu dùng carrier board khác, ArduPilot,
pin tự đóng hoặc flight controller khác thì phải kiểm tra lại pinout và giới hạn nguồn
theo đúng datasheet của thiết bị đó.

> **An toàn:** tháo cánh quạt và đặt drone trên bàn không dẫn điện trong toàn bộ quá
> trình cấu hình. Lần cấp nguồn đầu dùng nguồn bench có giới hạn dòng nếu có. Đo cực
> tính và điện áp tại đầu barrel trước khi cắm Jetson. Không vừa hàn vừa cắm pin.

## 1. Kiến trúc được chọn

```text
4S2P (12.0–16.8 V)
  ├─ cầu chì/anti-spark chính ─ PDB hoặc 4-in-1 ESC ─ motors
  ├─ power module tương thích 4S ─ POWER1 Pixhawk 6C
  └─ cầu chì nhánh ─ DC-DC ổn áp 12 V, >=5 A ─ barrel 5.5x2.5 mm ─ Jetson

Pixhawk 6C
  └─ TELEM2 ─ USB–UART 3.3 V ─ USB-A Jetson       (khuyến nghị trên drone)
     hoặc USB-C ─────────────── USB-A Jetson       (dễ thử trên bàn)

Jetson ─ Wi-Fi/4G ─ WSS ─ DroneOps backend
```

Jetson chỉ đọc telemetry và chuyển tiếp lên DroneOps. Gateway hiện tại có gửi
`REQUEST_DATA_STREAM` và `MAV_CMD_REQUEST_MESSAGE(AUTOPILOT_VERSION)` để xin dữ
liệu/UID; nó không arm, takeoff, land hoặc upload mission.

## Quick start — cách cài đơn giản

Phần này là tất cả những gì cần làm với phần mềm trong lần đầu. Các mục phía dưới
chỉ dùng khi cần hiểu phần cứng, cài thủ công hoặc xử lý lỗi.

### Lần đầu: cài một lần

1. Cắm Pixhawk vào Jetson, bảo đảm backend đang chạy và Jetson truy cập được backend.
2. Đưa `droneops-gateway.zip` lên Jetson. Có thể tải từ trang **Connect Drone**.
   Nếu web chưa chạy, mở terminal trên **máy đang chứa repo** (không phải terminal
   SSH của Jetson) và dùng `scp`:

   ```sh
   scp /Users/alexxxx/Desktop/SEP490/fe/public/downloads/droneops-gateway.zip \
     dronelab@IP_CUA_JETSON:/home/dronelab/
   ```

   Thay `IP_CUA_JETSON` bằng IP thật. Nếu không biết, chạy `hostname -I` trên Jetson.
   `scp` sẽ hỏi password của user `dronelab`.

3. Quay lại terminal Jetson, kiểm tra file trước khi giải nén:

   ```sh
   cd ~
   ls -lh droneops-gateway.zip
   mkdir -p ~/droneops-package
   unzip droneops-gateway.zip -d ~/droneops-package
   cd ~/droneops-package/droneops-gateway
   chmod +x install-jetson.sh pair-jetson.sh
   sudo ./install-jetson.sh
   ```

   Không chạy `unzip` nếu `ls` báo không có file. Nếu dùng trình duyệt để tải trực
   tiếp trên Jetson và file thật sự nằm trong `~/Downloads`, dùng
   `cd ~/Downloads` thay cho `cd ~`.

4. Installer sẽ hỏi bốn thông tin:

   - device Pixhawk: copy một dòng `/dev/serial/by-id/...` mà installer vừa in;
   - baud: nhấn Enter để dùng `921600` cho TELEM2;
   - system ID: nhấn Enter để dùng `1`;
   - backend: ví dụ `http://192.168.1.20:5159`.

5. Trên web tạo pairing code, sau đó chạy đúng một lệnh:

   ```sh
   sudo /opt/droneops-gateway/pair-jetson.sh
   ```

   Nhập pairing code khi được hỏi. Thành công thì helper tự enable và start service.

6. Kiểm tra:

   ```sh
   systemctl is-active droneops-gateway
   ```

   Kết quả phải là `active`; web sẽ hiện **Gateway online** rồi
   **Receiving drone telemetry**.

### Những lần bật nguồn sau

Không chạy lại installer. Jetson tự boot, tự nối Wi-Fi và service tự kết nối lại
Pixhawk/backend. Chỉ chạy lại lệnh pairing ở bước 5 khi session 12 giờ hết hạn, bị
Disconnect/Revoke hoặc đổi mission/UAV. Không cần chạy lại các lệnh Python dài trong
tài liệu.

Xem log nếu web không nhận dữ liệu:

```sh
sudo journalctl -u droneops-gateway -n 100 --no-pager
```

## 2. Kết nối MAVLink

### Phương án A — USB để bring-up

Dùng cáp **USB-A từ Jetson → USB-C vào Pixhawk**. Không dùng USB-C ↔ USB-C: USB-C
trên carrier board chuẩn của Orin Nano Dev Kit là cổng device/data, không phải cổng
host. Pixhawk thường xuất hiện là `/dev/ttyACM0` và USB MAVLink không dùng baud vật
lý (tham số `--baud` vẫn phải có nhưng không quyết định tốc độ USB).

Ưu điểm: ít dây, dễ chẩn đoán. Nhược điểm: đầu USB-C không khóa, dễ rung/lỏng và
đường USB có thể cấp ngược 5 V cho Pixhawk khi thử nguồn. Chỉ dùng cáp tốt, có strain
relief; không coi USB là nguồn bay chính của Pixhawk.

Trong QGroundControl kiểm tra:

- `SYS_USB_AUTO`: tự nhận hoặc MAVLink;
- `USB_MAV_MODE`: `Onboard` nếu firmware cung cấp lựa chọn này.

### Phương án B — TELEM2, khuyến nghị khi bay

Cách ít rủi ro nhất là dùng adapter USB–UART **3.3 V** có FTDI/CP210x thật và cắm
adapter vào USB-A của Jetson. Không nối UART 40-pin của Jetson trực tiếp nếu chưa có
level shifter phù hợp.

| Pixhawk 6C TELEM2 | Adapter USB–UART | Ghi chú |
|---|---|---|
| pin 1 `VCC +5V` | **không nối** | không cấp nguồn Jetson/adapter từ đây |
| pin 2 `TX` 3.3 V | `RX` | TX phải đi sang RX |
| pin 3 `RX` 3.3 V | `TX` | RX phải đi sang TX |
| pin 4 `CTS` | `RTS` | tùy chọn, chỉ khi dùng flow control |
| pin 5 `RTS` | `CTS` | tùy chọn, chỉ khi dùng flow control |
| pin 6 `GND` | `GND` | bắt buộc |

Gateway hiện không bật RTS/CTS, vì vậy cấu hình tối thiểu là TX, RX, GND. Dùng đầu
JST-GH đúng chuẩn, bọc và cố định dây; không dựa vào màu dây để xác định pin.

Trong QGroundControl đặt và reboot Pixhawk:

```ini
MAV_1_CONFIG=TELEM 2
MAV_1_MODE=Onboard
SER_TEL2_BAUD=921600
UXRCE_DDS_CFG=0
```

`921600` là cấu hình PX4 companion phổ biến hiện tại. Nếu adapter/cáp không ổn định,
dùng `57600` ở **cả** PX4 và `MAVLINK_BAUD`. Không để uXRCE-DDS hoặc driver khác
chiếm TELEM2. Sau reboot xác nhận có heartbeat trước khi lắp cánh.

## 3. Đấu nguồn pin 4S2P

Một pack 4S2P đã hoàn chỉnh vẫn có điện áp như 4S: khoảng 14.8 V danh định, 16.8 V
khi sạc đầy; `2P` chỉ tăng dung lượng/khả năng cấp dòng. Không lấy điện ở điểm giữa
cell và không tự ghép hai pack nếu không có thiết kế BMS/cell matching phù hợp.

1. Nhánh propulsion đi từ đầu pin chính qua anti-spark/cầu chì phù hợp tới PDB hoặc
   4-in-1 ESC.
2. Pixhawk nhận nguồn và đo pin qua **power module tương thích 4S** vào `POWER1`.
   Không đưa 12–16.8 V trực tiếp vào POWER1 (ngõ này cần 5 V đã điều áp).
3. Tạo nhánh riêng từ đầu pin chính, có cầu chì đặt sát điểm rẽ, vào DC-DC chất lượng
   tốt. Đặt đầu ra **12.0 V**, có công suất liên tục tối thiểu 60 W/5 A và chịu được
   transient/noise của hệ propulsion. Đầu ra đi vào barrel 5.5 × 2.5 mm của Jetson,
   **tâm dương, vỏ âm**.
4. Nối mass theo cấu trúc nhánh/star ở PDB. GND TELEM2 vẫn phải nối GND adapter để có
   mốc tín hiệu, nhưng không dùng dây GND tín hiệu nhỏ làm đường hồi nguồn Jetson.

Carrier chuẩn chấp nhận 9–20 V ở DC jack, nên pin 4S nằm trong dải điện áp trên giấy;
tuy nhiên **không khuyến nghị cắm pin thẳng** vì sụt áp, spike từ ESC, đấu ngược và
mất nguồn khi tải thay đổi. DC-DC giúp ổn định và tách nhiễu. Chọn cầu chì theo dòng
vào thực đo, tiết diện dây và định mức converter (không chọn chỉ theo con số 5 A đầu
ra). Kiểm tra nhiệt converter ở tải cao trước khi bay.

Không dùng:

- chân 5 V của TELEM2 để nuôi Jetson;
- USB-C của Jetson để cấp nguồn;
- BEC 5 V nhỏ dành cho servo;
- đầu barrel không rõ cực tính;
- nguồn chung không có cầu chì nhánh Jetson.

Tắt Jetson sạch bằng `sudo poweroff` trước khi rút pin trong giai đoạn thử nghiệm.
Khi vận hành thực tế nên thêm mạch hold-up/supervisor hoặc quy trình shutdown riêng;
systemd không thể bảo vệ filesystem khi nguồn bị cắt tức thời.

## 4. Cài thủ công và xử lý lỗi

Nếu Quick start đã thành công thì bỏ qua toàn bộ mục 4 và 5. Các bước sau dành cho
trường hợp không dùng installer hoặc cần chẩn đoán chi tiết.

### 4.1 Hệ điều hành và truy cập mạng

Flash JetPack/Jetson Linux theo tài liệu NVIDIA cho đúng revision của Dev Kit. Hoàn
tất OEM setup, tạo user quản trị, rồi cập nhật:

```sh
sudo apt update
sudo apt full-upgrade -y
sudo apt install -y python3-venv python3-pip git openssh-server chrony
sudo systemctl enable --now ssh chrony
```

Gateway không cần ROS, ROS 2, MAVSDK, MAVROS hay clone PX4-Autopilot. Chỉ cần thư
mục `gateway/` của repo DroneOps (hoặc file ZIP tải từ trang Connect Drone) và các
package trong `gateway/requirements.txt`.

### 4.2 Cài gateway

Trước tiên phải vào đúng thư mục chứa ba file nguồn. Chọn **một** trong hai cách:

**Nếu đã clone toàn bộ repo DroneOps** (thay `~/SEP490` bằng nơi clone thực tế):

```sh
cd ~/SEP490/gateway
```

**Nếu tải `droneops-gateway.zip` từ trang Connect Drone:**

```sh
cd ~/Downloads
unzip droneops-gateway.zip
cd droneops-gateway
```

Kiểm tra trước khi cài; lệnh này phải in đủ ba tên file và không báo lỗi:

```sh
pwd
ls -l gateway.py requirements.txt droneops-gateway.service.example
```

Chỉ tiếp tục sau khi kiểm tra trên thành công. Sau đó chạy:

```sh
sudo useradd --system --home /var/lib/droneops-gateway --create-home \
  --shell /usr/sbin/nologin droneops
sudo install -d -o droneops -g droneops /opt/droneops-gateway /var/lib/droneops-gateway
sudo install -m 0644 gateway.py requirements.txt droneops-gateway.service.example \
  /opt/droneops-gateway/
sudo chown -R droneops:droneops /opt/droneops-gateway /var/lib/droneops-gateway
sudo -u droneops python3 -m venv /opt/droneops-gateway/.venv
sudo -u droneops /opt/droneops-gateway/.venv/bin/pip install --upgrade pip
sudo -u droneops /opt/droneops-gateway/.venv/bin/pip install \
  -r /opt/droneops-gateway/requirements.txt
sudo usermod -aG dialout droneops
```

Nếu `useradd` báo user `droneops` đã tồn tại sau lần chạy trước thì có thể bỏ qua
riêng lỗi đó và tiếp tục từ lệnh `sudo install -d ...`. Nếu `ls` báo `No such file
or directory`, không chạy `cp/install` hay `pip` tiếp; tìm đúng nơi đã clone/giải nén:

```sh
find "$HOME" -maxdepth 4 -type f -name gateway.py 2>/dev/null
```

`pip install \` là một lệnh kéo dài sang dòng kế tiếp; phải chạy cùng dòng
`-r /opt/droneops-gateway/requirements.txt`, không chạy riêng dấu `\`.

Xác định cổng ổn định, không đoán `/dev/ttyACM0`:

```sh
ls -l /dev/serial/by-id/
udevadm info --query=property --name=/dev/ttyACM0 | \
  grep -E '^(ID_VENDOR_ID|ID_MODEL_ID|ID_SERIAL)='
```

Ưu tiên đường dẫn `/dev/serial/by-id/...`. Rút/cắm lại và xác nhận đường dẫn vẫn trỏ
đúng Pixhawk/adapter. Nếu thiết bị không có serial duy nhất, tạo udev rule theo
`ID_VENDOR_ID`, `ID_MODEL_ID` và `ID_SERIAL_SHORT`; không tạo rule chỉ khớp
`ttyACM*` vì có thể chọn nhầm thiết bị.

### 4.3 Test link trước khi ghép đôi

`REPLACE_ME` trong các ví dụ là placeholder, không phải tên device thật. Cắm Pixhawk
hoặc USB–UART vào Jetson rồi liệt kê cổng:

```sh
ls -l /dev/serial/by-id/ 2>/dev/null || true
ls -l /dev/ttyACM* /dev/ttyUSB* 2>/dev/null || true
sudo dmesg --ctime | tail -n 30
```

Thông thường USB trực tiếp tới Pixhawk là `ttyACM*`; adapter FTDI/CP210x là
`ttyUSB*`. Nếu `/dev/serial/by-id/` có kết quả, copy **nguyên đường dẫn ở phía bên
trái**, ví dụ:

```text
/dev/serial/by-id/usb-FTDI_FT232R_USB_UART_A10ABC-if00-port0
```

Đặt đường dẫn thật vào biến và xác nhận nó tồn tại. Không chạy tiếp nếu `test` không
in `Device exists`:

```sh
DEVICE='/dev/serial/by-id/THAY_BANG_TEN_THAT'
test -e "$DEVICE" && echo 'Device exists' || echo 'Device NOT found'
readlink -f "$DEVICE"
id droneops
```

Kết quả `id droneops` phải có group `dialout`. Nếu chưa có:

```sh
sudo usermod -aG dialout droneops
```

Sau đó test dưới đúng user của service. `sudo` mặc định không giữ biến shell, nên
truyền device thành đối số cho Python như dưới đây:

```sh
sudo -u droneops /opt/droneops-gateway/.venv/bin/python - "$DEVICE" 921600 <<'PY'
import sys
from pymavlink import mavutil
d, baud = sys.argv[1], int(sys.argv[2])
print('Opening:', d, 'baud:', baud)
m=mavutil.mavlink_connection(d, baud=baud)
h=m.wait_heartbeat(timeout=15)
if h is None:
    raise SystemExit('No MAVLink heartbeat within 15 seconds')
print('MAVLink heartbeat:', h.get_srcSystem(), h.get_srcComponent())
m.close()
PY
```

Với USB trực tiếp, có thể để `921600` hoặc `57600` vì USB CDC không dùng baud vật
lý. Với TELEM2 qua USB–UART, số baud ở lệnh phải trùng `SER_TEL2_BAUD` trên PX4.
Nếu không có device nào xuất hiện, thử cáp USB khác có data, đổi cổng USB-A, xác nhận
Pixhawk đã có nguồn và so sánh kết quả các lệnh trước/sau khi cắm. Nếu device tồn tại
nhưng báo `Permission denied`, kiểm tra lại group `dialout` và udev permissions. Nếu
mở được device nhưng timeout heartbeat, kiểm tra TX/RX chéo, GND, baud và cấu hình
MAVLink của PX4; đồng thời đóng QGroundControl/MAVProxy/process khác đang giữ cổng.

Nếu timeout: kiểm tra TX/RX chéo, GND, baud, `MAV_1_CONFIG`, rồi bảo đảm QGroundControl
hoặc process khác không giữ cổng.

## 5. Ghép đôi một lần và tự chạy khi boot

Trên web tạo pairing code cho đúng UAV/mission. Chạy tương tác lần đầu (USB thường
là baud `57600`; TELEM2 dùng baud đã đặt ở trên):

```sh
sudo -u droneops /opt/droneops-gateway/.venv/bin/python \
  /opt/droneops-gateway/gateway.py \
  --server https://API_CUA_BAN \
  --device /dev/serial/by-id/REPLACE_ME \
  --baud 921600 --system-id 1 --component-id 1 \
  --session-file /var/lib/droneops-gateway/session.private.json \
  --new-pairing
```

Tạo `/etc/droneops-gateway.env` (không thêm dấu nháy hoặc khoảng trắng quanh `=`):

```ini
DRONEOPS_SERVER=https://API_CUA_BAN
MAVLINK_DEVICE=/dev/serial/by-id/REPLACE_ME
MAVLINK_BAUD=921600
MAVLINK_SYSTEM_ID=1
MAVLINK_COMPONENT_ID=1
```

```sh
sudo install -m 0644 /opt/droneops-gateway/droneops-gateway.service.example \
  /etc/systemd/system/droneops-gateway.service
sudo chmod 600 /etc/droneops-gateway.env
sudo systemctl daemon-reload
sudo systemctl enable --now droneops-gateway
systemctl status droneops-gateway --no-pager
sudo journalctl -u droneops-gateway -n 100 --no-pager
```

Service chờ network-online, tự restart khi link/process lỗi và dùng session đã lưu.
Nó chỉ kết nối backend sau khi nhận được UID từ Pixhawk. Khi session 12 giờ hết hạn,
bị revoke, hoặc chuyển mission, tạo code mới, dừng service và chạy lại lệnh ghép đôi
với `--new-pairing`; sau đó start service.

Kiểm tra sau mỗi lần boot:

```sh
systemctl is-active droneops-gateway
journalctl -u droneops-gateway -b --no-pager
timedatectl status
```

## 6. Dùng Jetson không màn hình

### SSH qua LAN/Wi-Fi

Lấy IP trên Jetson lúc setup: `hostname -I`. Từ laptop cùng mạng:

```sh
ssh TEN_USER_JETSON@IP_CUA_JETSON
```

Nên đặt DHCP reservation trên router/hotspot hoặc dùng hostname mDNS
`ssh user@<hostname>.local`. Tạo khóa trên laptop bằng `ssh-keygen -t ed25519`, chép
khóa với `ssh-copy-id user@IP`, thử đăng nhập bằng khóa rồi mới cân nhắc tắt password
SSH. Không mở port 22 trực tiếp ra Internet; dùng VPN như WireGuard/Tailscale nếu cần
truy cập từ xa (phải cài và quản lý riêng).

Để Wi-Fi tự nối sau boot bằng NetworkManager:

```sh
nmcli device wifi list
sudo nmcli device wifi connect 'TEN_WIFI' password 'MAT_KHAU'
nmcli connection show
```

Lệnh chứa mật khẩu có thể đi vào shell history; dùng kết nối tạm khi nhập và xóa
dòng history nếu cần, hoặc cấu hình ban đầu qua giao diện/`nmtui`.

### Console cứu hộ khi chưa có mạng

USB-C của Jetson là device/debug, không dùng nó nối Pixhawk. Có thể nối USB-C Jetson
vào laptop để dùng debug/device mode tùy image, hoặc cách chắc chắn là cáp USB–TTL
3.3 V vào debug UART trên button header theo đúng Carrier Board Specification. Không
đấu 5 V của cáp TTL. Mở serial terminal `115200 8N1`, hoàn tất OEM setup/sửa Wi-Fi,
rồi quay lại SSH. Pin header thay đổi theo carrier board nên phải tra đúng revision,
không cắm theo vị trí đoán.

## 7. Checklist trước chuyến bay

- [ ] Cánh quạt tháo khi test; không có dây gần motor/prop.
- [ ] Đo barrel đúng tâm dương và 12.0 V ở pin đầy/lúc motor tải.
- [ ] Pixhawk dùng power module; Jetson dùng nhánh DC-DC và cầu chì riêng.
- [ ] TELEM2 pin 1 không nối; TX/RX chéo; GND tín hiệu đã nối.
- [ ] Device dùng `/dev/serial/by-id/...`, baud hai đầu giống nhau.
- [ ] `chrony` synchronized; Jetson vào được mạng backend qua WSS/TLS.
- [ ] `systemctl is-active droneops-gateway` trả `active` sau cold boot.
- [ ] Web hiện `Gateway online`, sau đó `Receiving drone telemetry`.
- [ ] Rút mạng rồi nối lại: service/WebSocket tự phục hồi.
- [ ] Thử nguồn và nhiệt ít nhất 15 phút; không brownout khi thay đổi throttle.
- [ ] Cáp có strain relief, DC-DC có thông gió, antenna không bị che.

## Tài liệu phần cứng chính thức

- [NVIDIA Orin Nano Dev Kit hardware layout](https://docs.nvidia.com/jetson/orin-nano-devkit/user-guide/latest/hardware_layout.html)
- [NVIDIA carrier board specification](https://developer.nvidia.com/downloads/assets/embedded/secure/jetson/orin_nano/docs/jetson_orin_nano_devkit_carrier_board_specification_sp.pdf)
- [Holybro Pixhawk 6C port pinout](https://docs.holybro.com/autopilot/pixhawk-6c/pixhawk-6c-ports)
- [Holybro Pixhawk 6C electrical specification](https://docs.holybro.com/autopilot/pixhawk-6c/technical-specification)
- [PX4 serial-port configuration](https://docs.px4.io/main/en/peripherals/serial_configuration)
- [PX4 companion-computer wiring](https://docs.px4.io/v1.14/en/companion_computer/pixhawk_companion)

