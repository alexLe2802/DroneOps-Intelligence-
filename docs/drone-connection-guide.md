# Kết nối drone với DroneOps

Backend production: **https://api.droneops.click**. Website: **https://droneops.click**.
Gateway chủ động gọi backend bằng HTTPS/WSS. Không nhập IP laptop/Jetson, không mở
port inbound trên router và không chạy backend .NET trên laptop khi dùng production.

## 1. Drone nào được nhận diện?

Hệ thống đọc `AUTOPILOT_VERSION.uid2`, hoặc `uid` khi uid2 không có, từ **flight
controller**. ID Jetson, USB/COM, telemetry radio và MAVLink system ID không thay
thế UID này. Nếu board không trả UID khác 0, việc ghép đôi bị từ chối.

Lần ghép đầu tiên khóa UID với một UAV trên web. Một UID không được ghép sang UAV
đăng ký khác; một UAV đã khóa không chấp nhận flight controller khác. Thay Jetson
hoặc radio nhưng giữ nguyên flight controller vẫn là cùng UAV. Thay flight
controller là thay định danh, cần quy trình quản trị kiểm tra riêng.

UID qua MAVLink không phải chứng thực chống giả mạo. Chỉ đăng ký gateway trên
Jetson/laptop được tin cậy; không chia sẻ file credential hoặc ZIP chứa session.

## 2. Có Jetson — cài một lần, bật nguồn là tự nối

### Đấu nối và chuẩn bị

1. Thử trên bàn khi tháo cánh. Dùng USB có truyền dữ liệu từ flight controller
   vào Jetson, hoặc TELEM2 qua USB–UART logic **3,3 V**. TELEM2 TX ↔ adapter RX,
   RX ↔ TX, GND ↔ GND; xác nhận pinout đúng board. Không nối chân nguồn 5 V khi
   dùng cấu hình UART ba dây; Jetson có nguồn riêng.
2. Cấu hình cổng flight controller cho MAVLink phù hợp. Setup mặc định dùng
   115200 baud, system ID 1; nếu thiết bị đã được cấu hình khác, thêm
   `--baud`/`--system-id` đúng vào lệnh cài lần đầu. Không đoán từ tên USB.
3. Lưu Wi‑Fi/Ethernet có Internet và bật tự kết nối trên Jetson. Nếu mạng chặn
   domain production, cần quản trị mạng cho phép các hostname DroneOps.

### Đăng ký lần đầu

1. Trên **Connect Drone**, chọn **JETSON ONBOARD**, đúng UAV và mission đã duyệt.
2. Xác nhận lần khóa phần cứng đầu tiên, tạo mã ghép có hiệu lực 5 phút.
3. SSH hoặc mở terminal Jetson **một lần**, dán lệnh **COPY JETSON COMMAND**.
   Lệnh tải package, tìm `/dev/serial/by-id`, cài router/gateway, bật service,
   ghép UID và lưu credential gateway. Nếu nhiều USB adapter, chọn đúng bằng số.
4. Khi thấy setup hoàn tất, đóng terminal/SSH. Không cần giữ log mở.
5. Trên web kiểm tra **Receiving drone telemetry**. Gateway online nhưng chưa có
   telemetry có thể nghĩa là flight controller chưa bật hoặc serial/MAVLink chưa đúng.

### Mỗi lần bật lại

Bật flight controller và Jetson, chờ mạng đã lưu. `droneops-router` và
`droneops-gateway` tự chạy bằng systemd, thử lại sau mất mạng/serial, đọc UID và
xin lease mission mới. Người dùng không cần chạy terminal.

Khi đổi mission, chọn UAV và mission mới trên web rồi bấm **CONNECT SELECTED
MISSION**. Gateway đã đăng ký có thể tự nhận connection này; không cần dán mã
mới vào Jetson. Nếu chưa chọn mission hợp lệ, gateway chờ; nó không tự tạo
mission, không chọn UAV khác và không cất cánh.

Credential đăng ký gateway là lâu dài và có thể thu hồi; lease mission vẫn có
thời hạn 12 giờ và được xin lại khi nối. Thu hồi gateway hoặc thay gateway cần
ghép một lần nữa. Cài gateway mới cho cùng UAV thay credential thiết bị cũ.

## 3. Không có Jetson — radio và laptop

1. Air radio nối cổng MAVLink của flight controller; ground radio tương ứng cắm
   USB laptop. Hai radio phải cùng cấu hình, drone và radio phải có nguồn.
2. Cài Python **3.9+**. Tải và giải nén **DOWNLOAD GATEWAY + SETUP GUIDE**.
3. Windows: mở **connect-drone.cmd**. macOS/Linux: mở terminal trong thư mục
   giải nén `droneops-gateway` và chạy:

   ```bash
   bash connect-drone.sh
   ```

4. Launcher tự cài môi trường Python riêng, liệt kê USB và dò heartbeat
   ArduPilot/PX4 ở các baud phổ biến. Nếu nhiều cổng USB, chỉ cần chọn số của
   ground radio. Không nhập `COM5`, system ID hoặc IP trong trường hợp tự dò được.
5. Chọn UAV/mission và tạo connection trên web. Lần đầu launcher hỏi mã ghép;
   nhập mã của đúng UAV. Nó đọc UID và lưu credential riêng cho controller đó.
6. Giữ cửa sổ launcher mở, laptop có Internet và không sleep. Những lần sau chỉ
   mở lại launcher; chọn mission trên web, không ghép lại chỉ vì đổi Wi‑Fi/IP.

Nếu dò thất bại: đóng Mission Planner/QGroundControl đang giữ serial, kiểm tra
USB, radio pairing, baud của cổng TELEM và nguồn. Dò baud không sửa được radio
chưa ghép hoặc port chưa bật MAVLink. Khi cần chẩn đoán vẫn có thể dùng CLI
`gateway.py --server ... --device ... --baud ... --system-id ...`; đây là chế độ nâng cao.

Muốn chạy GCS song song thì dùng MAVLink Router chia cổng cục bộ. Không cho
hai chương trình cùng mở trực tiếp serial radio. Endpoint GCS LAN là tính năng
riêng, không cần để kết nối website.

## 4. Ngắt kết nối, thay gateway và dữ liệu riêng

- **DISCONNECT**: thu hồi connection của mission hiện tại; gateway không được
  tự hồi sinh connection đó. Tạo connection mới trên web để cho phép nối lại.
- **FORGET TRUSTED GATEWAY**: thu hồi đăng ký lâu dài và tất cả connection của
  UAV; UID vẫn khóa với UAV. Muốn dùng lại cần ghép lần đầu trên thiết bị.
- Jetson giữ credential ở `/var/lib/droneops-gateway/session.private.json`.
  Laptop giữ riêng từng controller trong `.sessions/`. Không tải các file này lên
  web/chat/Git. ZIP do hệ thống phát hành không chứa credential.
- Pairing, gateway online hoặc xác nhận readiness không tự ARM/cất cánh.
  Thực thi theo lịch cần đủ consent, upload/read-back và toàn bộ dispatch gate.
  Xem [Vận hành bay tự động](automatic-flight-operations.md).

## 5. Chẩn đoán Jetson (chỉ khi cần)

```bash
systemctl is-enabled droneops-router droneops-gateway
systemctl is-active droneops-router droneops-gateway
journalctl -u droneops-gateway -n 30 --no-pager
```

Nếu đổi mật khẩu Wi‑Fi cần cập nhật mạng trên Jetson; service không thể tự biết
mật khẩu mới. Hệ thống không cần IP cố định của laptop/Jetson.

## 6. Triển khai phiên bản này

Migration bắt buộc, sau backup theo runbook deployment:
`20261104115300_gateway_enrollment_and_flight_preparation.sql`.
Không chạy migration ngầm ở startup. Update backend, frontend và gateway ZIP
cùng phiên bản; Jetson cũ phải chạy setup/ghép một lần để nhận credential đăng ký.

Kiểm thử software không thay thế test reboot Jetson, tháo/cắm serial, đổi Wi‑Fi,
lease hết hạn, chọn mission mới và thu hồi credential trên phần cứng thật.

## Appendix: Jetson hardware and manual router setup

### Chuẩn bị phần cứng một lần

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

### Cấu hình Pixhawk một lần

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

### Cài MAVLink Router

Chạy các lệnh trong mục này **trên Jetson qua terminal hoặc SSH**, không chạy trong PowerShell laptop. Router nhận MAVLink từ cổng Pixhawk và chia cho gateway/GCS. Không cần cắm Pixhawk để cài phần mềm router.

Installer `install-jetson.sh` đã tự cài router nếu thiếu. Chỉ làm thủ công bên dưới khi muốn chuẩn bị trước hoặc chẩn đoán lỗi cài đặt. Nếu đã có router, kiểm tra rồi chuyển sang mục 2:

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
