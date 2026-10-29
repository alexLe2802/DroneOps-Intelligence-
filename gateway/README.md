# Kết nối drone với DroneOps Web

Luồng: pilot tạo mã trên web → gateway Jetson ghép đôi qua WebSocket → nhận MAVLink qua UDP/USB → gửi
telemetry qua một kết nối WSS lâu dài → backend → Live Monitoring. Gateway nhận telemetry và có thể xin tần suất stream trên serial, không gửi lệnh
bay, không upload kế hoạch và không tự chuyển mission sang InProgress.

## Pilot sử dụng

1. Đăng nhập pilot, mở **Connect Drone** (hoặc Assigned UAV → Connect a Drone).
2. Chọn UAV Active của mình và mission Approved/InProgress gán cho UAV đó.
3. Bấm **Create pairing code**. Mã có 16 ký tự hex, hết hạn sau 5 phút và dùng một lần.
   Tạo mã mới sẽ thu hồi phiên trước của cùng UAV.
4. Trên máy mặt đất, tải gateway từ liên kết trong trang, giải nén và cài Python 3.9+.
5. Cài dependency trong virtual environment, từ thư mục vừa giải nén:

   ```sh
   python -m venv .venv
   # Windows: .venv\Scripts\activate
   # macOS/Linux: source .venv/bin/activate
   python -m pip install -r requirements.txt
   ```

6. Chạy một trong hai cách sau. Thay địa chỉ API và MAVLink system ID bằng cấu hình
   của bạn. Đây là địa chỉ backend, không phải frontend port 3000.

   **Mission Planner chuyển tiếp UDP:** cấu hình MAVLink forwarding đến máy gateway,
   ví dụ UDP 14550. Nếu cùng máy, dùng loopback:

   ```sh
   python gateway.py --server https://YOUR-BACKEND --device udpin:127.0.0.1:14550 --system-id 1
   ```

   Nếu Mission Planner ở máy khác, chọn IP interface nhận UDP của gateway, ví dụ
   `udpin:192.168.1.20:14550`. Chỉ mở port đó trên mạng tin cậy. Không để hai chương
   trình cùng chiếm UDP port hoặc serial port.

   **USB/serial trực tiếp:** đóng chương trình khác đang giữ cổng serial. Chọn đúng
   tên cổng và baud của thiết bị, ví dụ Windows COM3 hoặc Linux /dev/ttyUSB0:

   ```sh
   python gateway.py --server https://YOUR-BACKEND --device /dev/ttyACM0 --baud 57600 --system-id 1
   ```

7. Nhập mã khi gateway hỏi. Mã được nhập ẩn; không cần integration key dùng chung.
8. Trên web, **Gateway online** nghĩa là máy gateway đã liên lạc với server;
   **Receiving drone telemetry** nghĩa là gateway đã nhận mẫu từ autopilot phù hợp.
   Mở Live Monitoring để xem vị trí, pin, mode và các mẫu mới nhất.
9. Bấm **Disconnect** trên web để thu hồi credential. Dừng gateway bằng Ctrl+C chỉ
   dừng chương trình; muốn ngắt quyền gửi hoàn toàn thì thu hồi trên web.

## Jetson Orin Nano (Ubuntu/JetPack)

Hướng dẫn đầy đủ về USB/TELEM2, sơ đồ nguồn pin 4S2P, cài JetPack, tự khởi động và
truy cập headless nằm tại
[`docs/jetson-orin-nano-pixhawk-6c-setup.md`](../docs/jetson-orin-nano-pixhawk-6c-setup.md)
(trong gói tải về, mở `JETSON_SETUP.md`).
Lưu ý nhanh: USB phải đi từ **USB-A host của Jetson sang USB-C của Pixhawk**; không
nối hai cổng USB-C. Khi dùng TELEM2 qua USB–UART, không nối chân +5 V của TELEM2.

### Quick start trên Jetson

Trong thư mục vừa giải nén, chạy:

```sh
chmod +x install-jetson.sh pair-jetson.sh
sudo ./install-jetson.sh
```

Installer tự cài gateway, hỏi device/baud/IP backend và tạo service. Sau đó tạo mã
pairing trên web và chạy:

```sh
sudo /opt/droneops-gateway/pair-jetson.sh
```

Pair thành công thì service tự bật ở mọi lần boot sau. Không chạy lại installer.
Chỉ chạy lại `pair-jetson.sh` khi session 12 giờ hết hạn, bị revoke hoặc đổi
mission/UAV. Phần lệnh dài dưới đây là cách cài thủ công/xử lý lỗi.

Cài lần đầu và ghép đôi tương tác trước khi bật service:

```sh
# Trước hết cd vào thư mục gateway/ của repo, hoặc thư mục droneops-gateway/
# vừa giải nén. Ba file ở lệnh ls phải tồn tại.
ls -l gateway.py requirements.txt droneops-gateway.service.example
sudo useradd --system --home /var/lib/droneops-gateway --create-home --shell /usr/sbin/nologin droneops
sudo mkdir -p /opt/droneops-gateway /var/lib/droneops-gateway
sudo install -m 0644 gateway.py requirements.txt droneops-gateway.service.example /opt/droneops-gateway/
sudo chown -R droneops:droneops /opt/droneops-gateway /var/lib/droneops-gateway
sudo -u droneops python3 -m venv /opt/droneops-gateway/.venv
sudo -u droneops /opt/droneops-gateway/.venv/bin/pip install -r /opt/droneops-gateway/requirements.txt
sudo -u droneops /opt/droneops-gateway/.venv/bin/python /opt/droneops-gateway/gateway.py \
  --server https://YOUR-BACKEND --device /dev/ttyACM0 --baud 57600 --system-id 1 \
  --session-file /var/lib/droneops-gateway/session.private.json --new-pairing
```

Sau khi ghép đôi thành công, tạo `/etc/droneops-gateway.env`:

```ini
DRONEOPS_SERVER=https://YOUR-BACKEND
MAVLINK_DEVICE=/dev/ttyACM0
MAVLINK_BAUD=57600
MAVLINK_SYSTEM_ID=1
MAVLINK_COMPONENT_ID=1
```

Trên thiết bị thật, thay `/dev/ttyACM0` bằng đường dẫn ổn định trong
`/dev/serial/by-id/` để service không nối nhầm cổng sau reboot.

Sao chép `droneops-gateway.service.example` vào `/etc/systemd/system/droneops-gateway.service`,
cho user `droneops` quyền đọc cổng serial (thường là group `dialout`), rồi bật service:

```sh
sudo usermod -aG dialout droneops
sudo chmod 600 /etc/droneops-gateway.env
sudo systemctl daemon-reload
sudo systemctl enable --now droneops-gateway
sudo journalctl -u droneops-gateway -f
```

Backend production phải được reverse proxy bằng TLS và cho phép WebSocket Upgrade tại
`/ws/drone-gateway`. Không bật `--allow-insecure-websocket` ngoài mạng phát triển cô lập.

## Chạy backend trên máy phát triển và cho Jetson kết nối qua LAN

Repo dùng port backend mặc định **5159**. `localhost:5159` chỉ truy cập được từ chính
máy chạy backend; Jetson phải dùng **IPv4 LAN của máy đó**, ví dụ
`http://192.168.1.20:5159`. Đây là IP/port backend, không phải frontend port 3000 và
không phải IP của Jetson.

### 1. Cho backend lắng nghe trên mạng LAN

Bảo đảm máy đã có .NET 8 SDK và cấu hình database trong
`be/DroneOps/DroneOps.API/appsettings.Local.json` hoặc biến môi trường
`ConnectionStrings__DefaultConnection`. Từ thư mục gốc repo chạy:

```sh
cd be/DroneOps
ASPNETCORE_ENVIRONMENT=Development dotnet run \
  --project DroneOps.API/DroneOps.API.csproj \
  --no-launch-profile --urls http://0.0.0.0:5159
```

PowerShell:

```powershell
cd be/DroneOps
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project DroneOps.API/DroneOps.API.csproj `
  --no-launch-profile --urls http://0.0.0.0:5159
```

Nếu repo đang dùng SDK cục bộ `.tools/dotnet`, có thể dùng script sẵn có và truyền
đối số ứng dụng sau `--`:

```sh
./be/DroneOps/run-api.sh -- --urls http://0.0.0.0:5159
```

Log phải có dòng tương tự `Now listening on: http://0.0.0.0:5159`. Không dùng
launch profile `http` mặc định cho trường hợp này vì profile bind
`http://localhost:5159`.

### 2. Lấy đúng IP của máy chạy backend

Máy backend và Jetson phải ở cùng Wi-Fi/LAN, và access point không bật client/AP
isolation. Lấy địa chỉ của card mạng đang kết nối:

```sh
# macOS, thường là Wi-Fi en0
ipconfig getifaddr en0

# Linux
hostname -I

# Windows PowerShell
ipconfig
```

Chọn IPv4 private dạng `192.168.x.x`, `10.x.x.x` hoặc `172.16–31.x.x`; không chọn
`127.0.0.1`, địa chỉ VPN/Docker hoặc IPv6 link-local. Ví dụ máy backend trả về
`192.168.1.20` thì URL Jetson phải là `http://192.168.1.20:5159`.

Nên tạo DHCP reservation trên router để IP máy backend không đổi. Nếu IP đổi thì
phải sửa `DRONEOPS_SERVER` trên Jetson và restart service.

### 3. Mở firewall chỉ trong mạng tin cậy

Cho phép inbound TCP 5159 cho private/trusted LAN. Trên macOS, khi được hỏi hãy cho
phép kết nối đến `dotnet`, hoặc mở **System Settings → Network → Firewall → Options**.
Trên Ubuntu có thể dùng:

```sh
sudo ufw allow from 192.168.1.0/24 to any port 5159 proto tcp
```

Thay subnet bằng mạng thực tế. Trên Windows tạo inbound rule TCP 5159 cho profile
**Private**, không dùng profile Public. Không port-forward 5159 trên router và không
phơi HTTP development ra Internet.

Từ một máy khác trong cùng LAN, kiểm tra trước:

```sh
curl -i http://192.168.1.20:5159/ws/drone-gateway
```

Kết quả `400 Bad Request` kèm thông báo yêu cầu WebSocket là **đúng**: nó chứng minh
đã đến được endpoint; request `curl` thường không phải WebSocket upgrade.

### 4. Kiểm tra từ Jetson và ghép đôi

SSH vào Jetson rồi kiểm tra route/IP:

```sh
ip route
ping -c 3 192.168.1.20
curl -i --connect-timeout 5 http://192.168.1.20:5159/ws/drone-gateway
```

Sau đó dừng service và ghép đôi một lần. Vì đây là HTTP/WS thuần trong LAN dev, cờ
`--allow-insecure-websocket` là bắt buộc:

```sh
sudo systemctl stop droneops-gateway
sudo -u droneops /opt/droneops-gateway/.venv/bin/python \
  /opt/droneops-gateway/gateway.py \
  --server http://192.168.1.20:5159 \
  --device /dev/serial/by-id/REPLACE_ME \
  --baud 921600 --system-id 1 --component-id 1 \
  --session-file /var/lib/droneops-gateway/session.private.json \
  --new-pairing --allow-insecure-websocket
```

Thay IP, device và baud bằng giá trị thật. Mã pairing được tạo trên web và chỉ có
hiệu lực 5 phút.

### 5. Cho service Jetson dùng IP backend LAN

Tạo hoặc sửa `/etc/droneops-gateway.env`:

```ini
DRONEOPS_SERVER=http://192.168.1.20:5159
MAVLINK_DEVICE=/dev/serial/by-id/REPLACE_ME
MAVLINK_BAUD=921600
MAVLINK_SYSTEM_ID=1
MAVLINK_COMPONENT_ID=1
```

Service mẫu mặc định cố ý không cho WS không mã hóa. Với môi trường LAN dev, tạo
override để thêm cờ cho riêng máy này:

```sh
sudo systemctl edit droneops-gateway
```

Nhập nguyên khối sau:

```ini
[Service]
ExecStart=
ExecStart=/opt/droneops-gateway/.venv/bin/python /opt/droneops-gateway/gateway.py --server ${DRONEOPS_SERVER} --device ${MAVLINK_DEVICE} --baud ${MAVLINK_BAUD} --system-id ${MAVLINK_SYSTEM_ID} --component-id ${MAVLINK_COMPONENT_ID} --session-file /var/lib/droneops-gateway/session.private.json --allow-insecure-websocket
```

Áp dụng và xác minh:

```sh
sudo systemctl daemon-reload
sudo systemctl restart droneops-gateway
systemctl status droneops-gateway --no-pager
sudo journalctl -u droneops-gateway -n 100 --no-pager
```

Web phải lần lượt hiện **Gateway online** và **Receiving drone telemetry**. Nếu
`curl` timeout thì kiểm tra backend bind/firewall/IP trước; nếu `curl` được nhưng
gateway báo không có UID/heartbeat thì kiểm tra Pixhawk, device và baud. Khi deploy
thật, dùng domain HTTPS/WSS với certificate hợp lệ, bỏ override
`--allow-insecure-websocket`, và đặt `DRONEOPS_SERVER=https://...`.

Ví dụ location Nginx (dùng chung certificate HTTPS hiện có):

```nginx
location /ws/drone-gateway {
    proxy_pass http://127.0.0.1:5159;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_set_header Host $host;
    proxy_read_timeout 60s;
    proxy_send_timeout 60s;
}
```

“Không dùng HTTP” trong tài liệu này áp dụng cho data plane Jetson → backend:
pair/resume/heartbeat/telemetry đều đi qua WSS. Các thao tác người dùng trên web như tạo
mã ghép đôi, xem trạng thái và thu hồi phiên vẫn là API HTTPS thông thường.

## Credential, kết nối lại và giới hạn

- Phiên sống 12 giờ. Credential chỉ gắn một owner/UAV/mission; backend kiểm tra lại
  quyền sở hữu UAV, trạng thái Active và trạng thái mission mỗi lần gửi.
- Token được lưu dạng hash trong DB. Gateway lưu token riêng trong
  `session.private.json`, không ghi vào log/command line. Giữ file trong tài khoản
  hệ điều hành của pilot; không đồng bộ/chia sẻ. Trên POSIX file được đặt mode 0600.
  Windows cần giữ thư mục dưới profile có ACL riêng của người dùng.
- Khởi động lại gateway tự dùng credential đã lưu. Khi bị thu hồi/hết hạn, tạo mã
  mới trên web và chạy với `--new-pairing`. Không chia sẻ file credential giữa UAV.
- WSS mặc định cho Jetson/máy ở xa. Localhost WS được hỗ trợ để dev. Mạng LAN dev
  có thể dùng `--allow-insecure-websocket` một cách tường minh; backend phải bind IP
  mà máy gateway truy cập được. FE server dùng biến **API_BASE_URL**, không dùng
  NEXT_PUBLIC_API_BASE_URL để định tuyến JWT proxy.
- Chọn đúng `--system-id` và `--component-id` (mặc định component 1). Gateway bỏ qua
  các ID khác và heartbeat GCS; các ID này là bộ lọc routing, không phải chứng thực
  phần cứng. Dùng link MAVLink/mạng được quản lý; phần này không cấp danh tính phần
  cứng mật mã cho autopilot.
- Với serial trực tiếp, gateway gửi REQUEST_DATA_STREAM (2 Hz) sau heartbeat
  đúng autopilot để xin dữ liệu. Không gửi arm/takeoff/land hay upload mission. UDP
  forwarding dùng stream có sẵn từ Mission Planner.
- Mẫu tối đa khoảng 1 Hz; gateway gửi heartbeat trong cùng WebSocket 10 giây/lần. Mất autopilot heartbeat
  quá 5 giây thì ngừng tạo mẫu. Giá trị cảm biến quá 5 giây trở thành null.
  WebSocket đứt tạm thời sẽ kết nối lại với backoff; sample giữ cùng Idempotency-Key và backlog quá 10 giây
  bị bỏ để không thể hiện dữ liệu cũ thành live. Credential lỗi 401/403 thì dừng.
- SourceTimestamp là giờ UTC của gateway khi gom mẫu, không phải đồng hồ boot của
  autopilot. Cần đồng bộ giờ máy gateway. Vị trí dùng GLOBAL_POSITION_INT, relative
  altitude mm→m, vận tốc cm/s→m/s; pin dùng SYS_STATUS. Không có dữ liệu thì không
  dựng giá trị giả. Khi GLOBAL_POSITION_INT chưa có tọa độ dùng được, gateway thử
  GPS_RAW_INT còn mới (tối đa 5 giây), có fix 3D trở lên; không dùng độ cao GPS MSL
  thay cho relative altitude. Mỗi 10 giây Terminal in fix_type, số vệ tinh và tuổi
  dữ liệu GPS để chẩn đoán. Đây không phải bảo đảm độ chính xác vị trí.
  Thời tiết không lấy từ nhiệt độ bo mạch.
- MISSION_CURRENT.seq được cộng 1 để chuyển index zero-based sang waypoint web.
  Mission Planner/autopilot có home hoặc command phụ phải được đối chiếu với route
  web trước khi dùng chỉ số tiến độ. Ghép đôi không xác minh kế hoạch onboard khớp
  kế hoạch trên web và không upload/thay đổi kế hoạch onboard.
- Gateway là chương trình chạy trên máy mặt đất, không phải browser WebUSB. Không
  có auto-installer hoặc điều khiển cổng serial từ xa qua trình duyệt.

## Backend và migration

Giữ cấu trúc **Controller → IDroneConnectionService → IDroneConnectionRepository**.
Controller không dùng AppDbContext. API pilot dùng JWT role uav_operator:

| Method | Route | Chức năng |
|---|---|---|
| GET | /api/drone-connections | Phiên của pilot hiện tại |
| POST | /api/drone-connections | Tạo mã cho `{uavId, missionId}` |
| DELETE | /api/drone-connections/{id} | Thu hồi phiên của mình |
| WebSocket | /ws/drone-gateway | Pair/resume session, heartbeat, telemetry và ACK trên một kết nối WSS |

WebSocket handshake được rate limit theo remote IP (60 kết nối/phút); mỗi connection
chấp nhận tối đa 240 message/phút. Nếu deploy nhiều API instance, cấu hình thêm limiter ở gateway
mạng dùng chung. Không cho phép proxy browser chuyển tiếp các route gateway.
Bảng mới bật RLS và không cấp quyền anon/authenticated; backend là nơi kiểm tra scope.

Áp dụng migration trước khi chạy phiên bản này:

```sh
dotnet run --project be/DroneOps/DroneOps.API --no-launch-profile -- --migrate-drone-connections --environment Development
```

Không tự chạy migration khi API startup. Khi rollback code, giữ bảng lịch sử;
không DROP nếu chưa có kế hoạch xử lý các phiên/credential đang dùng.

## Kiểm thử

```sh
python -m unittest discover -s gateway -p 'test_*.py'
dotnet run --project be/DroneOps/DroneOps.AuthChecks -- --drone-connections /absolute/path/to/repo
```

Để chạy thêm WebSocket + UDP + DB + SSE smoke test với backend local port 5159 đang chạy,
đặt DRONEOPS_GATEWAY_PYTHON thành đường dẫn Python của virtualenv có pymavlink rồi
chạy lại --drone-connections. Test tạo dữ liệu riêng, không dùng mission của pilot thật,
và dọn fixture trong finally. Unit test transport dùng UDP loopback và serial PTY
ảo trên POSIX; không phải bằng chứng đã thử với radio/drone vật lý.

Đóng gói lại download sau khi sửa source:

```sh
python gateway/package.py
```

Nguồn giao thức: [MAVLink Python guide](https://mavlink.io/en/mavgen_python/),
[common messages](https://mavlink.io/en/messages/common.html).

Có thể kiểm tra HTTP/SSE/UDP cô lập không cần DB ngoài:

```sh
# DRONEOPS_GATEWAY_PYTHON trỏ đến Python đã cài requirements.txt
# PowerShell: $env:DRONEOPS_GATEWAY_PYTHON = 'C:\path\to\.venv\Scripts\python.exe'
# macOS/Linux: export DRONEOPS_GATEWAY_PYTHON=/path/to/.venv/bin/python
dotnet run --project be/DroneOps/DroneOps.TelemetryChecks -- --gateway /absolute/path/to/repo
```

Suite này dùng Kestrel thật, WebSocket, MAVLink UDP và gateway Python thật, EF InMemory cùng
repository test double. PostgreSQL repository được kiểm tra riêng bằng AuthChecks.

## Danh tính thiết bị vật lý

Mỗi UAV registry chỉ gắn một `DroneHardwareIdentity`, và một hardware UID chỉ gắn
một UAV trong toàn hệ thống. Gateway đọc `AUTOPILOT_VERSION` từ đúng autopilot trước
khi ghép đôi: dùng `uid` 64-bit khác 0; nếu không có thì dùng `uid2` 18 byte khác 0.
Giữ ưu tiên UID64 ổn định khi chuyển MAVLink 1/2. Không dùng COM port, system ID,
tên gateway hoặc UUID tự sinh thay cho UID phần cứng.

Lần đầu, pilot xác nhận bản đăng ký trên web. Ghép đôi thành công ghi binding lâu
dài. Những lần sau server đối chiếu lại UID; cùng drone không thể chọn một UAV khác
trong ba bản đăng ký. Đổi mission hoặc thu hồi phiên không xóa binding. UAV có
binding không thể bị xóa cứng để lách quy tắc; có thể deactivate. Không có API tự
xóa/đổi UID. Thay flight controller hoặc firmware làm UID thay đổi cần quy trình
quản trị có đối chiếu thực tế; không tự động ghi đè binding.

Khi tạo mã cho mission mới, chạy gateway với `--new-pairing`. Khi chỉ khởi động lại
cùng phiên, bỏ cờ này để dùng credential đã lưu. Gateway vẫn đọc lại UID trước khi
sử dụng credential; nếu khác thiết bị thì dừng. Khi serial reconnect, gateway đọc
lại UID; trong khi chạy định kỳ xin AUTOPILOT_VERSION và dừng nếu UID thay đổi.

Định danh này nhận biết flight controller. Nó chưa chứng minh toàn bộ khung drone
hoặc giấy tờ đăng ký và chưa phải chứng thực phần cứng chống giả mạo: dữ liệu MAVLink
không ký và gateway do pilot kiểm soát vẫn có thể khai báo giả UID. Nếu cần chống
cố ý giả mạo phải bổ sung chuỗi tin cậy thiết bị/gateway và MAVLink signing hoặc
chứng chỉ thiết bị phù hợp, không coi UID/system ID là secret.

Migration `drone_hardware_identity` thu hồi các phiên cũ chưa có UID quan sát được;
pilot phải ghép đôi lại. Migration không suy đoán UID cho dữ liệu cũ. Áp dụng bằng
cùng lệnh `--migrate-drone-connections` (chạy cả hai migration theo thứ tự).
