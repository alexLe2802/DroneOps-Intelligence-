# DroneOps onboard gateway

Hướng dẫn sử dụng duy nhất: [Kết nối drone và Jetson](../docs/drone-connection-guide.md).
Trong ZIP tải về, mở **CONNECTION_SETUP.md**.

Pixhawk -> MAVLink Router -> UDP localhost -> gateway -> WSS backend -> SSE web.
Router tùy chọn gửi MAVLink đến QGroundControl laptop. Profile radio không có Jetson
chạy gateway trực tiếp trên laptop qua serial của ground radio hoặc UDP forwarding.
Không cần camera. Xem phần profile radio trong hướng dẫn chung.

## Cài lần đầu trên Jetson: làm theo thứ tự

Lệnh pairing không phải lệnh cài đặt. Trước khi chạy nó, phải nối phần cứng,
xác nhận serial path rồi cài gateway. Các lệnh dưới đây chạy trên Jetson/SSH vào
Jetson, không chạy trong PowerShell laptop.

dotnet run --project be/DroneOps/DroneOps.API --no-launch-profile -- --urls http://0.0.0.0:5159 --environment Development

### 1. Nối Pixhawk với Jetson

Tháo cánh và ngắt nguồn trước khi đấu dây. Chọn một cách:

- Thử trên bàn: USB Pixhawk -> cáp USB có truyền dữ liệu -> USB-A Jetson.
  Dùng cáp từng kết nối thành công Pixhawk với laptop. Cáp chỉ sạc không dùng được.
- Cấu hình onboard: TELEM2 Pixhawk -> adapter USB-UART logic 3,3 V -> USB-A Jetson.
  TELEM2 pin 2 TX nối RX adapter, pin 3 RX nối TX, pin 6 GND nối GND.
  Pin 1 nguồn 5 V không nối; pin 4/5 để trống với cấu hình ba dây và tắt flow control.
  Xác nhận chiều pin theo tài liệu board. Jetson dùng nguồn riêng, không lấy từ TELEM2.

Nếu nối TELEM2 vào GPIO Jetson thì đây là cách UART riêng cần cấu hình pinmux,
console và xác nhận chân/mức logic trước. Không nhập đoán ttyTHS1/ttyTHS2 chỉ vì
chúng có sẵn; chúng không chứng minh Pixhawk đang được nối.

### 2. Tìm và xác nhận serial path

Sau khi cắm, chạy:

```bash
lsusb
ls -l /dev/serial/by-id/
ls /dev/ttyACM* /dev/ttyUSB* 2>/dev/null
```

Ưu tiên đường đầy đủ `/dev/serial/by-id/TEN_THIET_BI_THUC_TE` được liệt kê.
USB Pixhawk thường tạo `/dev/ttyACM0`, adapter USB-UART thường tạo `/dev/ttyUSB0`;
đây là ví dụ, không nhập nếu thiết bị chưa xuất hiện. Rút/cắm và so sánh danh sách
để xác nhận đúng thiết bị. Có serial chưa đồng nghĩa đã nhận MAVLink hợp lệ.

Nếu không có cổng, **dừng cài**, kiểm tra dây/cổng/nguồn. Xem sự kiện mới:

```bash
sudo dmesg -w
```

Rút/cắm đúng đầu USB Pixhawk hoặc adapter ở Jetson rồi xem dòng mới. Nhấn Ctrl+C
để thoát. Chỉ thấy USB hub, Bluetooth hay đầu thu chuột nghĩa là chưa thấy thiết bị
drone; không dùng chúng làm serial path. Cài router/gateway không sửa được cáp
không truyền dữ liệu hoặc phần cứng chưa được hệ điều hành nhận.

### 3. Cài gateway từ ZIP

Chép droneops-gateway.zip đã tải từ Connect Drone vào home Jetson, rồi chạy:

```bash
sudo apt update
sudo apt install -y unzip
unzip droneops-gateway.zip -d ~/droneops-package
cd ~/droneops-package/droneops-gateway
sudo bash install-jetson.sh
```

Nếu đã giải nén gói cũ, dùng thư mục giải nén mới hoặc thay đúng file từ ZIP mới.
Installer hỏi:

- Serial path: đường đã xác nhận ở bước 2, không phải UID, IP hay mã pairing.
- Baud: khớp cổng TELEM2/autopilot và adapter, mặc định 115200. Xem hướng dẫn chung
  để cấu hình MAVLink theo PX4 hoặc ArduPilot; QGroundControl không xác nhận firmware.
- System ID: ID MAVLink thực tế của autopilot, không đoán từ cổng USB.
- Backend origin: ví dụ http://192.168.1.82:5159, backend port chứ không phải frontend port 3000.
- IP laptop GCS: tùy chọn, để trống nếu chỉ xem web.

Installer cài MAVLink Router nếu thiếu và bật hai service. Mạng cần được cấu hình
tự kết nối trên Ubuntu. Chi tiết cài router thủ công nằm trong CONNECTION_SETUP.md.

### 4. Ghép đôi sau khi cài thành công

Trên Connect Drone chọn UAV/mission và tạo mã 5 phút. Chạy:

```bash
sudo bash /opt/droneops-gateway/pair-jetson.sh
```

Nhập mã khi được hỏi. Helper đọc UID Pixhawk, lưu session và chuyển sang service.
Nếu file không tồn tại, bước cài đặt chưa hoàn tất; chạy lại installer ở bước 3.

### 5. Những lần sau

Trong cùng phiên còn hợp lệ, chỉ cần bật nguồn Pixhawk/Jetson và mạng đã lưu có
đường đến backend. Service tự chạy và nối lại khi mất mạng, không cần giữ SSH.
Phiên hết hạn 12 giờ, revoke hoặc đổi mission cần mã mới và chạy lại bước 4.
Không tự pair chỉ nhờ có Internet.

#### Backend trên laptop và kết nối ở LAN khác

Mỗi lần sử dụng, backend trên laptop phải đang chạy. Từ thư mục gốc repo trên
laptop, chạy lệnh sau (giữ terminal mở):

```powershell
dotnet run --project be/DroneOps/DroneOps.API --no-launch-profile -- --urls http://0.0.0.0:5159 --environment Development
```

`0.0.0.0` là địa chỉ **lắng nghe** để backend nhận kết nối LAN; không phải IP cần
đặt cho laptop hoặc nhập vào Jetson. `localhost` chỉ cho truy cập từ chính laptop.
Jetson dùng IP LAN thật của laptop để gọi backend.

Nếu vẫn cùng LAN/IP và phiên còn hợp lệ, không cần sửa cấu hình hoặc pair lại.
Khi chuyển sang LAN khác (ví dụ đến lab), không cần chạy lại installer:

1. Kết nối laptop và Jetson vào mạng mới, bật tự kết nối cho Wi-Fi Jetson.
2. Chạy `ipconfig` trên laptop, lấy IPv4 của card đang nối LAN lab.
3. Trên Jetson chạy `sudo nano /etc/droneops-gateway.env`, đổi backend origin.
   Ví dụ nếu laptop là `192.168.10.20`:

   ```ini
   DRONEOPS_SERVER=http://192.168.10.20:5159
   DRONEOPS_TRANSPORT_FLAG=--allow-insecure-websocket
   ```

   HTTP chỉ dùng cho LAN dev tin cậy. Giữ port 5159 nếu backend vẫn chạy port đó.
   Không nhập `0.0.0.0`, `localhost` hoặc IP Jetson vào backend origin.
4. Nếu dùng QGroundControl, chạy `sudo nano /etc/droneops-router/main.conf`.
   Chỉ đổi `Address` trong `[UdpEndpoint laptop]` thành IP laptop mới, giữ port
   `14550`, rồi chạy `sudo systemctl restart droneops-router`. Không đổi endpoint
   gateway nội bộ `127.0.0.1:14551`. Nếu không dùng GCS thì bỏ bước này.
5. Kiểm tra từ Jetson bằng IP laptop mới:

   ```bash
   curl --connect-timeout 5 -i http://192.168.10.20:5159/ws/drone-gateway
   ```

   HTTP 426 với `Use the WebSocket protocol` xác nhận đã tới endpoint. Nếu timeout,
   kiểm tra backend đang chạy, Windows Firewall và mạng lab có chặn client-to-client
   không. Không cần tắt toàn bộ firewall.
6. Vì backend origin đã đổi, tạo mã mới trên web và chạy lại helper pairing ở bước 4.
   Helper đọc cấu hình mới và khởi động gateway. Nếu chỉ IP Jetson đổi còn backend
   origin giữ nguyên, không cần pair lại chỉ vì đổi IP Jetson.

Tóm tắt: backend **nghe** `0.0.0.0:5159`; Jetson **gọi** `http://IP_LAPTOP:5159`;
Laptop IPv4 cho GCS là `IP_LAPTOP` không kèm scheme/port. Với backend cloud có domain
HTTPS cố định, đổi LAN thường chỉ cần Jetson có Internet; endpoint GCS LAN vẫn cập
nhật riêng nếu dùng. Không để cờ insecure khi chuyển sang cấu hình HTTPS production.

#### Kiểm tra service

```bash
systemctl is-active droneops-router droneops-gateway
journalctl -u droneops-router -n 30 --no-pager
journalctl -u droneops-gateway -n 30 --no-pager
```

Không chạy gateway và GCS trực tiếp trên cùng serial: router giữ serial và chia
UDP cho từng chương trình. Gateway chỉ telemetry, không arm/cất cánh/upload mission.

## Nếu không có Jetson

Chọn Radio telemetry trên Connect Drone. Air radio nối TELEM2, ground radio nối
USB laptop; gateway chạy trên laptop có Internet. Không chạy installer Jetson.
Làm theo phần profile radio trong CONNECTION_SETUP.md/hướng dẫn chung.

Developer checks:

```sh
python -m pip install -r gateway/requirements.txt
python -m unittest discover -s gateway -p 'test_*.py'
python gateway/package.py
dotnet run --project be/DroneOps/DroneOps.TelemetryChecks -- --gateway /absolute/repo
```

Đặt DRONEOPS_GATEWAY_PYTHON trỏ đến Python có dependencies cho loopback gateway.
PTY chỉ chạy POSIX; loopback không chứng minh phần cứng thật. Không commit token.
