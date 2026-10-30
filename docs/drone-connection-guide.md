# Kết nối drone với DroneOps: Jetson hoặc radio telemetry

## Chọn profile

| Profile trên Connect Drone | Gateway chạy ở đâu? | Cần giữ thiết bị nào hoạt động? |
|---|---|---|
| Jetson onboard | Jetson gắn trên drone | Jetson, Pixhawk và đường mạng đến backend |
| Radio telemetry | Laptop nối USB với radio dưới đất | Laptop không sleep, gateway, radio hai đầu, Pixhawk và Internet laptop |

Cả hai dùng cùng gói gateway, pairing UAV/mission, hardware UID và WebSocket/WSS đến backend. Chọn profile chỉ thay hướng dẫn cài đặt, không thay quyền hay thu hồi phiên. Không cần camera cho cả hai. Với Jetson, làm theo phần 1-10 bên dưới. Với radio, dùng phần **Profile radio telemetry** ngay sau đây, không chạy installer Jetson.

## Profile radio telemetry (không cần Jetson)

```text
Autopilot TELEM2 -> Radio trên drone ~~ sóng ~~ Radio dưới đất
                                                    |
                                                   USB
                                                    |
                                            Laptop: gateway
                                                    |
                                                   WSS
                                                    |
                                            Backend -> Web
```

1. Nối air radio vào TELEM2 bằng dây đúng pinout/điện áp của autopilot và radio. Không áp dụng sơ đồ USB-UART ba dây của Jetson nguyên xi cho radio cần nguồn. Kiểm tra công suất cổng trước khi cấp radio từ TELEM2.
2. Cấu hình cổng autopilot phát MAVLink; baud serial của radio và autopilot phải khớp. Ghép/config hai radio theo nhà sản xuất. Đây là radio truyền MAVLink, không phải mặc định receiver ELRS hoặc USB joystick của Pocket.
3. Cắm ground radio vào laptop qua USB. Xem COM trong Device Manager Windows hoặc `/dev/serial/by-id/` trên Linux. Radio phải truyền được heartbeat và phản hồi AUTOPILOT_VERSION qua đường hai chiều để gateway kiểm tra UID.
4. Cài Python 3.9+, tải ZIP từ Connect Drone, giải nén. Mở terminal trong thư mục droneops-gateway. Ví dụ Windows:

```powershell
python -m venv .venv
.venv\Scripts\python.exe -m pip install -r requirements.txt
.venv\Scripts\python.exe gateway.py --server https://YOUR-BACKEND --device COM5 --baud 57600 --system-id 1 --new-pairing
```

COM5, 57600, system ID và URL chỉ là ví dụ; thay bằng cấu hình thật. Không cần activate virtualenv khi dùng đường Python đầy đủ. Linux/macOS:

```bash
python3 -m venv .venv
.venv/bin/python -m pip install -r requirements.txt
.venv/bin/python gateway.py --server https://YOUR-BACKEND --device /dev/ttyUSB0 --baud 57600 --system-id 1 --new-pairing
```

Linux cần quyền nhóm dialout (đăng nhập lại sau khi thêm nhóm). macOS dùng đường `/dev/cu.*` thật. HTTP chỉ cho dev LAN với cờ `--allow-insecure-websocket` có chủ đích.

5. Trên Connect Drone chọn **Radio telemetry**, UAV và mission; tạo mã rồi nhập terminal. Gateway giữ tiến trình chạy để gửi telemetry; không đóng laptop/sleep/terminal. Khi restart cùng phiên hợp lệ, bỏ `--new-pairing`. Phiên hết hạn 12 giờ, revoke hoặc đổi mission cần mã mới. File session.private.json chứa credential riêng, không chia sẻ/commit.
6. Web phải chuyển thành Receiving drone telemetry. Laptop cần Internet đến backend; drone chỉ cần link radio tới laptop. Radio không tự cấp Internet cho laptop.

### Dùng GCS đồng thời trên laptop

Chỉ một chương trình giữ serial radio. Cách đơn giản: Mission Planner giữ COM radio và bật MAVLink UDP forwarding đến `127.0.0.1:14551`. Gateway đọc endpoint riêng:

```powershell
.venv\Scripts\python.exe gateway.py --server https://YOUR-BACKEND --device udpin:127.0.0.1:14551 --system-id 1 --new-pairing
```

Đường forwarding cần truyền được cả phản hồi/yêu cầu UID. Nếu cấu hình forwarding không cho gateway gửi yêu cầu ngược, dùng MAVLink Router làm chủ serial, chia hai endpoint: GCS `127.0.0.1:14550`, gateway `127.0.0.1:14551`. Trên Linux/macOS có router, ví dụ:

```bash
mavlink-routerd -t 0 -e 127.0.0.1:14550 -e 127.0.0.1:14551 /dev/ttyUSB0:57600
```

QGroundControl nghe UDP 14550, gateway nghe 14551. Thay device/baud thật. Không mở cùng COM trong GCS và gateway. Không chạy installer Jetson trên laptop Windows. Chỉ cần web thì gateway đọc serial trực tiếp là đủ.

---

## Profile Jetson onboard

Hướng dẫn thống nhất cho drone không camera: cài một lần, ghép đôi cho UAV/mission, sau đó bật nguồn. Jetson tự chạy và tự nối lại khi mạng đến backend hoạt động. Đây là telemetry giám sát, chưa phải điều khiển bay tự động.

## 1. Luồng hoạt động

```text
Pocket -> ELRS receiver -> Pixhawk 6C -> ESC / động cơ
                              |
                       TELEM2 / USB-UART
                              |
                      Jetson: MAVLink Router
                        |                |
                UDP 127.0.0.1:14551   UDP qua LAN (tùy chọn)
                        |                |
                     Gateway        QGroundControl laptop:14550
                        |
                       WSS
                        |
                  Backend -> Database -> SSE -> Web
```

Chỉ router giữ serial; không cần Mission Planner/laptop chạy để web nhận dữ liệu nếu Jetson truy cập được backend. Camera không cần thiết. Bind Pocket không tự tạo cầu MAVLink qua USB tay cầm.

Internet không thay quyền ghép đôi. Lần đầu, đổi mission hoặc khi phiên 12 giờ hết hạn/bị thu hồi cần mã mới từ web. Mã sống 5 phút, dùng một lần. Tạo mã mới thu hồi phiên trước của UAV. Mất mạng tạm thời không cần pair lại nếu phiên còn hợp lệ. Hệ thống không tự chọn UAV/mission.

## 2. Chuẩn bị phần cứng một lần

- Jetson Orin Nano Developer Kit NVIDIA có JetPack tương thích, SSD 256 GB, tản nhiệt/quạt.
- Pixhawk 6C đã cấu hình bay, receiver ELRS hoạt động.
- USB-UART có TX/RX logic 3,3 V (không phải RS-232), dây TELEM2.
- Nguồn DC-DC riêng cho Jetson, mạng Wi-Fi/router hoặc modem.

SSD 256 GB đủ cho hệ điều hành, gateway, telemetry và phát triển ban đầu. Giữ khoảng 40-50 GB trống, giới hạn log. Kiểm tra `df -h` và `findmnt /`: gắn NVMe không tự chuyển root filesystem từ microSD. Cài JetPack/NVMe theo hướng dẫn NVIDIA, sao lưu trước khi flash.

LiPo 4S thường là 14,8 V danh định, 16,8 V đầy. Dùng DC-DC ổn áp riêng đúng yêu cầu carrier board và tải Jetson. Không cấp Jetson từ TELEM. Developer kit NVIDIA dùng DC jack 5,5 x 2,5 mm; USB-C là dữ liệu. Đối chiếu revision, điện áp/cực tính bằng tài liệu và adapter gốc. Thử trên bàn bằng adapter gốc trước.

Tháo cánh, ngắt nguồn khi đấu. TELEM2 Pixhawk 6C:

| Pin | Tín hiệu | USB-UART |
|---|---|---|
| 1 | +5 V | Không nối |
| 2 | TX 3,3 V | RX |
| 3 | RX 3,3 V | TX |
| 4, 5 | CTS, RTS | Không nối trong cấu hình ba dây |
| 6 | GND | GND |

Cắm adapter vào Jetson. Xác nhận chiều pin theo Holybro, không đoán theo màu dây. Cấp Pixhawk qua power module. USB Pixhawk-Jetson trực tiếp chỉ dùng chẩn đoán bàn trước khi xác nhận hành vi firmware.

## 3. Cấu hình Pixhawk một lần

QGroundControl không xác nhận firmware là PX4: xem tên/phiên bản trước, sao lưu tham số. Không flash firmware chỉ để làm theo ví dụ.

Nếu là PX4, chọn TELEM2 và một MAVLink instance còn trống, ví dụ:

```text
MAV_1_CONFIG = TELEM 2
MAV_1_MODE = Onboard
SER_TEL2_BAUD = 115200
MAV_1_RATE = 5000
MAV_1_FLOW_CTRL = Force off (nếu phiên bản có tham số này)
```

Chọn tên tùy chọn thay vì đoán enum; lưu/reboot/kiểm tra lại. Không cho driver khác giữ TELEM2. RATE là giới hạn B/s, không phải Hz. Baud installer phải khớp Pixhawk: nếu giữ 921600 thì nhập 921600 khi cài.

Nếu là ArduPilot: kiểm tra mapping TELEM2/SERIAL2, ví dụ `SERIAL2_PROTOCOL=2`, `SERIAL2_BAUD=115` cho 115200; tắt hardware flow control tương ứng khi dùng ba dây. Không dùng tham số PX4 trên ArduPilot.

## 4. Cho Jetson nhớ mạng

Kết nối Wi-Fi trong Ubuntu Settings, bật tự kết nối. Hoặc dùng terminal:

```bash
nmcli device wifi list
sudo nmcli --ask device wifi connect "TEN_WIFI"
nmcli connection show
sudo nmcli connection modify "TEN_KET_NOI" connection.autoconnect yes
sudo timedatectl set-ntp true
```

Mật khẩu nhập tương tác. Tên kết nối lấy từ danh sách, có thể khác SSID. Jetson tự nối mạng đã lưu, không tự biết mật khẩu mạng mới. Đồng hồ phải đúng để telemetry không bị coi là cũ/tương lai.

Backend production cần HTTPS/WSS, certificate hợp lệ và reverse proxy WebSocket Upgrade tại `/ws/drone-gateway`. Nếu backend chạy trên laptop, laptop và backend phải bật, bind IP mạng, firewall cho phép. Internet không tự truy cập backend localhost. Không mở cổng MAVLink trực tiếp ra Internet.

## 5. Cài một lần trên Jetson

### 5.1. Cài MAVLink Router

Chạy các lệnh trong mục này **trên Jetson qua terminal hoặc SSH**, không chạy trong PowerShell laptop. Router nhận MAVLink từ cổng Pixhawk và chia cho gateway/GCS. Không cần cắm Pixhawk để cài phần mềm router.

Installer `install-jetson.sh` đã tự cài router nếu thiếu. Chỉ làm thủ công bên dưới khi muốn chuẩn bị trước hoặc chẩn đoán lỗi cài đặt. Nếu đã có router, kiểm tra rồi chuyển sang mục 5.2:

```bash
command -v mavlink-routerd
mavlink-routerd --version
```

**Cách A - qua apt nếu kho Ubuntu có gói:**

```bash
sudo apt update
sudo apt install -y mavlink-router
```

Nếu hiện `Unable to locate package mavlink-router`, dùng cách B. Đây không nhất thiết là lỗi mạng: kho Ubuntu/JetPack của bạn có thể không cung cấp gói. Trên Jetson của bạn chạy Ubuntu 24.04 ARM64, lần cài thực tế đã phải dùng cách B.

**Cách B - build từ repository chính thức:**

```bash
sudo apt update
sudo apt install -y git meson ninja-build pkg-config gcc g++ libsystemd-dev

mkdir -p ~/droneops-tools
git clone --recurse-submodules https://github.com/mavlink-router/mavlink-router.git ~/droneops-tools/mavlink-router
cd ~/droneops-tools/mavlink-router

meson setup build .
ninja -C build
sudo ninja -C build install
```

Nếu thư mục source đã tồn tại từ lần trước, không clone đè hoặc xóa thư mục. Vào source hiện có rồi tiếp tục:

```bash
cd ~/droneops-tools/mavlink-router
git submodule update --init --recursive
# Chỉ chạy lệnh setup nếu chưa có thư mục build đã cấu hình thành công:
meson setup build .
ninja -C build
sudo ninja -C build install
```

Nếu build đã cấu hình thành công, bỏ lệnh `meson setup` và chạy từ `ninja`. Nếu setup báo thiếu dependency, sửa lỗi được in ra rồi chạy lại; không chạy bước install khi build thất bại. Không cần `sudo` cho clone/setup/build, chỉ dùng cho apt và install.

**Xác nhận sau cài:**

```bash
command -v mavlink-routerd
mavlink-routerd --version
git -C ~/droneops-tools/mavlink-router rev-parse HEAD
```

Lệnh Git cuối chỉ áp dụng cách B; lưu commit để tái tạo môi trường. Binary có thể nằm ở `/usr/bin/mavlink-routerd` hoặc `/usr/local/bin/mavlink-routerd` tùy cấu hình build. Dùng kết quả `command -v`, không mặc định một đường dẫn. Lần cài thực tế trên Jetson của bạn nằm ở `/usr/bin/mavlink-routerd`.

Cài binary chưa có nghĩa router đã kết nối Pixhawk. Chưa bật service `mavlink-router` mặc định chỉ để thử: installer DroneOps sẽ tạo cấu hình `/etc/droneops-router/main.conf` và service **`droneops-router`** riêng sau khi bạn chọn serial/baud. Không chạy hai router cùng giữ cổng Pixhawk.

Hướng dẫn upstream: [MAVLink Router - Compilation and Installation](https://github.com/mavlink-router/mavlink-router#compilation-and-installation).

### 5.2. Cài gateway và bật service

Tải **Gateway + setup guide** từ Connect Drone. Chép ZIP lên Jetson bằng USB/SCP. Mở terminal tại thư mục chứa ZIP:

```bash
sudo apt update
sudo apt install -y unzip
unzip droneops-gateway.zip -d ~/droneops-package
cd ~/droneops-package/droneops-gateway
sudo bash install-jetson.sh
```

Installer hỏi: đường `/dev/serial/by-id/...` của adapter, baud, system ID và backend origin (ví dụ `https://api.example.com`, không phải frontend port 3000). Chỉ chọn HTTP khi thử LAN tin cậy và xác nhận `LAN`.

Nếu cần QGroundControl, nhập IPv4 laptop cùng LAN; để trống nếu chỉ xem web. Đặt DHCP reservation cho laptop. Installer cần mạng để tải dependencies. Nếu Ubuntu không có gói router, nó build upstream và báo vị trí source để ghi lại commit.

Sau cài, hai service `droneops-router` và `droneops-gateway` tự chạy khi boot. Chưa pair thì gateway chờ credential, không hỏi mã trong background. Tắt chương trình khác đang giữ serial. Cài lại giữ session nhưng thay server/thiết bị có thể cần pair lại.

## 6. Ghép đôi cho phiên nhiệm vụ

1. Bật Pixhawk, Jetson, mạng; xác nhận đúng drone.
2. Trên Connect Drone chọn UAV Active và mission Approved/InProgress của UAV.
3. Xác nhận binding lần đầu và tạo mã khi Jetson đã sẵn sàng.
4. Chạy trên Jetson:

```bash
sudo /opt/droneops-gateway/pair-jetson.sh
```

Nhập mã. Helper chỉ pair, lưu credential rồi thoát; service chạy ngay sau đó. Không cần giữ SSH/terminal mở. UID được đọc từ Pixhawk; system ID/tên cổng không thay UID. Không chia sẻ/commit `/var/lib/droneops-gateway/session.private.json`.

## 7. Những lần sau: bật nguồn và có mạng

Không chạy lại installer/helper nếu phiên vẫn hợp lệ. Router/gateway tự chạy; mạng mất rồi trở lại sẽ reconnect có backoff, không đưa backlog cũ lên như live. Cổng serial xuất hiện muộn được service thử lại.

| Trạng thái web | Ý nghĩa / thao tác |
|---|---|
| Waiting for Jetson | Chưa pair; chạy helper |
| Jetson online, waiting for drone | Backend thấy gateway, chưa có mẫu mới |
| Receiving drone telemetry | Dữ liệu mới đang đến; mở Live Monitoring |
| Offline | Kiểm tra nguồn, mạng, backend và service |
| Session expired / Disconnected | Tạo mã mới và chạy helper |

Disconnect thu hồi quyền phiên; tắt nguồn chỉ dừng gửi. Pair không arm, không cất cánh, không upload mission hoặc tự chuyển mission sang InProgress.

## 8. QGroundControl tùy chọn

Bật UDP nghe 14550 trên laptop, cho phép firewall trên mạng Private. Laptop có IP đã nhập lúc cài và có đường LAN từ Jetson. Rút USB laptop-Pixhawk để xác nhận dữ liệu vẫn đến. Không cho hai ứng dụng giữ cùng UDP port.

Đổi IP laptop: sửa endpoint laptop trong `/etc/droneops-router/main.conf`, rồi `sudo systemctl restart droneops-router`. Đây không phải IP backend. Wi-Fi tích hợp chỉ nên dùng thử gần trước; tầm bay/độ tin cậy cần đo kiểm. Dùng 4G/5G thì GCS cần VPN/routing nếu muốn nhận MAVLink; web chỉ cần Jetson đến được backend.

## 9. Chẩn đoán nhanh

```bash
systemctl is-active droneops-router droneops-gateway
journalctl -u droneops-router -n 50 --no-pager
journalctl -u droneops-gateway -n 50 --no-pager
ls -l /dev/serial/by-id/
timedatectl status
df -h
```

- Chờ UID/heartbeat: kiểm tra nguồn Pixhawk, TX/RX/GND, baud, stream và flow control. Autopilot phải trả lời AUTOPILOT_VERSION qua router.
- WebSocket unavailable: kiểm tra URL/backend, mạng, TLS và proxy Upgrade.
- Expired/revoked/no longer eligible: chọn đúng mission/UAV rồi pair lại; reconnect không vượt thu hồi quyền.
- GCS có dữ liệu nhưng web không có: kiểm tra gateway, session và đồng hồ.
- Có telemetry nhưng chưa vị trí: GPS chưa fix hoặc dữ liệu cũ, không tạo tọa độ giả.
- Jetson reboot: kiểm tra DC-DC, tải và nhiệt trước; không tắt failsafe để vượt lỗi.

Đổi backend: sửa `/etc/droneops-gateway.env` bằng sudo rồi pair lại. HTTP LAN cần `DRONEOPS_TRANSPORT_FLAG=--allow-insecure-websocket`, HTTPS để rỗng. Không sửa token thủ công.

## 10. Trước khi bay và phát triển tiếp

Thử bàn khi tháo cánh: cold boot tự chạy; mất/nối lại mạng; rút/cắm adapter; đúng UID; token hết hạn; Pixhawk còn hoạt động khi Jetson tắt. Cấu hình failsafe mất RC/data link đúng firmware, có mode Pocket giành lại điều khiển. Kiểm tra tải trọng, trọng tâm, nhiệt và nguồn trên khung drone thật.

Không camera không cản telemetry hoặc phát triển waypoint trong tương lai. Gateway chưa có module điều khiển tự động. Bước sau là SITL rồi module riêng qua endpoint router, Guided/Offboard theo firmware, với kiểm tra quyền và failsafe. Vòng giữ ổn định nằm ở Pixhawk, không phụ thuộc Internet.

Nguồn: [Holybro pinout](https://docs.holybro.com/autopilot/pixhawk-6c/pixhawk-6c-ports), [NVIDIA hardware](https://docs.nvidia.com/jetson/orin-nano-devkit/user-guide/latest/hardware_layout.html), [PX4 MAVLink](https://docs.px4.io/main/en/peripherals/mavlink_peripherals), [ArduPilot serial](https://ardupilot.org/copter/docs/common-telemetry-port-setup.html), [MAVLink Router](https://github.com/mavlink-router/mavlink-router).

Tài liệu phần mềm không thay kết quả kiểm thử phần cứng thực tế.
