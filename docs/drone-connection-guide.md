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
- Pairing, gateway online hoặc xác nhận readiness đều không ARM/cất cánh.
  Luồng điều khiển tự động được mô tả trong `docs/automatic-flight-proposal.md`.

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
