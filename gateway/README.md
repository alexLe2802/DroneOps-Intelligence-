# Kết nối drone với DroneOps Web

Luồng: pilot tạo mã trên web → gateway ghép đôi → nhận MAVLink qua UDP/USB → gửi
telemetry vào backend → Live Monitoring. Gateway nhận telemetry và có thể xin tần suất stream trên serial, không gửi lệnh
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
   python gateway.py --api https://YOUR-BACKEND --device udpin:127.0.0.1:14550 --system-id 1
   ```

   Nếu Mission Planner ở máy khác, chọn IP interface nhận UDP của gateway, ví dụ
   `udpin:192.168.1.20:14550`. Chỉ mở port đó trên mạng tin cậy. Không để hai chương
   trình cùng chiếm UDP port hoặc serial port.

   **USB/serial trực tiếp:** đóng chương trình khác đang giữ cổng serial. Chọn đúng
   tên cổng và baud của thiết bị, ví dụ Windows COM3 hoặc Linux /dev/ttyUSB0:

   ```sh
   python gateway.py --api https://YOUR-BACKEND --device COM3 --baud 57600 --system-id 1
   ```

7. Nhập mã khi gateway hỏi. Mã được nhập ẩn; không cần integration key dùng chung.
8. Trên web, **Gateway online** nghĩa là máy gateway đã liên lạc với server;
   **Receiving drone telemetry** nghĩa là gateway đã nhận mẫu từ autopilot phù hợp.
   Mở Live Monitoring để xem vị trí, pin, mode và các mẫu mới nhất.
9. Bấm **Disconnect** trên web để thu hồi credential. Dừng gateway bằng Ctrl+C chỉ
   dừng chương trình; muốn ngắt quyền gửi hoàn toàn thì thu hồi trên web.

## Credential, kết nối lại và giới hạn

- Phiên sống 12 giờ. Credential chỉ gắn một owner/UAV/mission; backend kiểm tra lại
  quyền sở hữu UAV, trạng thái Active và trạng thái mission mỗi lần gửi.
- Token được lưu dạng hash trong DB. Gateway lưu token riêng trong
  `session.private.json`, không ghi vào log/command line. Giữ file trong tài khoản
  hệ điều hành của pilot; không đồng bộ/chia sẻ. Trên POSIX file được đặt mode 0600.
  Windows cần giữ thư mục dưới profile có ACL riêng của người dùng.
- Khởi động lại gateway tự dùng credential đã lưu. Khi bị thu hồi/hết hạn, tạo mã
  mới trên web và chạy với `--new-pairing`. Không chia sẻ file credential giữa UAV.
- HTTPS mặc định cho máy ở xa. Localhost HTTP được hỗ trợ để dev. Mạng LAN dev
  có thể dùng `--allow-insecure-http` một cách tường minh; backend phải bind IP
  mà máy gateway truy cập được. FE server dùng biến **API_BASE_URL**, không dùng
  NEXT_PUBLIC_API_BASE_URL để định tuyến JWT proxy.
- Chọn đúng `--system-id` và `--component-id` (mặc định component 1). Gateway bỏ qua
  các ID khác và heartbeat GCS; các ID này là bộ lọc routing, không phải chứng thực
  phần cứng. Dùng link MAVLink/mạng được quản lý; phần này không cấp danh tính phần
  cứng mật mã cho autopilot.
- Với serial trực tiếp, gateway gửi REQUEST_DATA_STREAM (2 Hz) sau heartbeat
  đúng autopilot để xin dữ liệu. Không gửi arm/takeoff/land hay upload mission. UDP
  forwarding dùng stream có sẵn từ Mission Planner.
- Mẫu tối đa khoảng 1 Hz; gateway heartbeat 10 giây/lần. Mất autopilot heartbeat
  quá 5 giây thì ngừng tạo mẫu. Giá trị cảm biến quá 5 giây trở thành null.
  Lỗi HTTP tạm thời retry có backoff và cùng Idempotency-Key; backlog quá 10 giây
  bị bỏ để không thể hiện dữ liệu cũ thành live. Credential lỗi 401/403 thì dừng.
- SourceTimestamp là giờ UTC của gateway khi gom mẫu, không phải đồng hồ boot của
  autopilot. Cần đồng bộ giờ máy gateway. Vị trí dùng GLOBAL_POSITION_INT, relative
  altitude mm→m, vận tốc cm/s→m/s; pin dùng SYS_STATUS. Không có dữ liệu thì không
  dựng giá trị giả. Thời tiết không lấy từ nhiệt độ bo mạch.
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
| POST | /api/drone-gateway/pair | Đổi mã + gatewayName/systemId/componentId lấy credential |
| POST | /api/drone-gateway/{id}/heartbeat | Bearer token riêng của gateway |
| POST | /api/drone-gateway/{id}/samples | Bearer token + Idempotency-Key + DTO telemetry hiện có |

Pair endpoint công khai được rate limit theo remote IP (10 request/phút); ingest
240 request/phút/IP. Nếu deploy nhiều API instance, cấu hình thêm limiter ở gateway
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

Để chạy thêm HTTP + UDP + DB + SSE smoke test với backend local port 5159 đang chạy,
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

Suite này dùng Kestrel thật, MAVLink UDP và gateway Python thật, EF InMemory cùng
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
