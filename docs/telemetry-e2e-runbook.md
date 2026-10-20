# KAN-44 — PX4 → gateway → API → web runbook

Runbook này dùng cho smoke/integration test trước buổi bay thử. Kết quả fixture không phải là bằng chứng phần cứng. Chỉ đánh dấu hardware verified sau khi đã ghi lại PX4 firmware, transport/baud, source mapping và thời điểm chạy với thiết bị thật.

## 1. Luồng và trách nhiệm

```text
PX4 / simulator
  └─ MAVLink (USB serial, radio hoặc UDP)
       └─ gateway/forwarder của đội tích hợp
            └─ POST /api/integrations/telemetry/samples
                 ├─ PostgreSQL TelemetryRecord (history + latest projection)
                 ├─ GET /api/missions/{id}/telemetry/latest
                 └─ GET /api/live/stream?missionId={id} (SSE, không phải video)
                      └─ web browser
```

Gateway MAVLink/raw và mapping source-to-UAV do task gateway cung cấp. Repository này kiểm thử từ HTTP ingest boundary trở đi bằng fixture có nguồn `sim-uav-001`; không tuyên bố đã decode raw MAVLink. Nếu gateway chưa được bàn giao, ghi blocker thay vì nối PX4 trực tiếp vào API JSON.

## 2. Biến môi trường và secret

Không commit integration key, JWT key, mật khẩu database hoặc ảnh quay có header/token. Cấu hình runtime tối thiểu:

```powershell
$env:ConnectionStrings__DefaultConnection = '<postgres connection string>'
$env:IntegrationSettings__ApiKey = '<random integration key>'
$env:Jwt__Key = '<at least 32-byte signing key>'
$env:Jwt__Issuer = 'DroneOps.API'
$env:Jwt__Audience = 'DroneOps.Client'
```

Chạy migration SQL theo thứ tự trong `database/migrations` nếu database mới hoặc schema chưa cập nhật. Kiểm tra connection trước:

```powershell
psql $env:ConnectionStrings__DefaultConnection -f database/check-connection.sql
```

## 3. Khởi động và network path

Backend:

```powershell
cd be/DroneOps
dotnet restore DroneOps.sln
dotnet run --project DroneOps.API/DroneOps.API.csproj --urls http://0.0.0.0:5000
```

Frontend:

```powershell
cd fe
npm ci
$env:NEXT_PUBLIC_API_BASE_URL = 'http://<backend-lan-ip>:5000'
npm run dev -- --hostname 0.0.0.0
```

Gateway trên máy đất phải gọi `http://<backend-lan-ip>:5000`, không dùng `localhost` nếu backend ở máy khác. Trình duyệt gọi frontend bằng `http://<frontend-lan-ip>:3000`. Cho phép TCP 5000 và 3000 qua firewall trên mạng tin cậy; không public integration endpoint ra Internet.

## 4. PX4 / Mission Planner transport

- Ghi lại PX4 firmware và vehicle ID trước test.
- USB/radio serial chỉ có một process được giữ COM port. Nếu Mission Planner đang mở port, gateway không được mở cùng port.
- Phương án khuyến nghị: Mission Planner giữ serial và forward MAVLink qua UDP đến gateway; hoặc đóng Mission Planner rồi để gateway giữ serial.
- Ghi rõ `COMx` và baud (thường theo telemetry radio/thiết bị, không tự suy đoán), hoặc UDP bind/target IP và port.
- Mapping phải khớp `sourceId → UAV ID`; mission phải tồn tại và được gán đúng UAV. Sai mapping phải trả HTTP 400.
- Gateway gửi `Authorization: Bearer <integration-key>` và một `Idempotency-Key` ổn định cho mỗi MAVLink sample. Retry phải tái sử dụng cùng key.

## 5. Automated integration checks (fixture)

Runner dùng controller/middleware thật, in-memory DB cô lập và Kestrel loopback; không truy cập production DB hay thiết bị:

```powershell
cd be/DroneOps
dotnet run --project DroneOps.TelemetryChecks/DroneOps.TelemetryChecks.csproj
```

Kỳ vọng: `26 telemetry end-to-end checks passed`. Các check bao phủ:

- missing/invalid credential, tọa độ sai và mission/UAV mapping sai;
- nullable GPS được lưu `null`, không biến thành `0`;
- ingest hợp lệ, duplicate retry chỉ lưu một bản ghi, out-of-order không đè latest;
- tọa độ/đơn vị/progress đi qua DB và latest API;
- packet loss chuyển snapshot thành stale/offline nhưng giữ last-known value;
- reconnect trả online, và kết nối SSE mới nhận latest snapshot trước.

## 6. Smoke test với gateway thật

1. Chọn một mission `In Progress` đã gán đúng UAV và mở màn hình live monitoring.
2. Khởi động backend, frontend, sau đó gateway. Lưu log đã che secret.
3. Arm simulator/device theo quy trình an toàn. Xác nhận marker, latitude/longitude, altitude reference, m/s, battery %, mode, armed và waypoint sequence.
4. So sánh `sourceTimestamp` và `receivedAt`; ghi latency end-to-end. Không dùng đồng hồ máy chưa đồng bộ.
5. Gửi lại cùng packet/idempotency key: API trả 200 và `duplicate: true`, DB không tăng thêm record.
6. Tạm dừng forwarding trên gateway (không tắt backend): UI/latest phải chuyển Online → Stale → Offline và vẫn đánh dấu dữ liệu cũ.
7. Bật forwarding lại: sample mới phải xuất hiện và kết nối SSE mới phải nhận latest snapshot trước event tiếp theo.
8. Thử credential sai, mapping sai và payload ngoài range; ghi status/body đã loại secret.
9. Dừng test có kiểm soát. Mất link không được tự động chuyển mission thành Completed.

## 7. Thu thập evidence và xử lý lỗi

Evidence tối thiểu: commit SHA, ngày giờ UTC, người chạy, fixture hay hardware, PX4 firmware, transport/baud, source/UAV/mission ID, network path, kết quả runner, latency và lỗi/blocker. Không lưu key/token/raw connection string.

Lỗi thường gặp:

- `401`: thiếu/sai integration key; kiểm tra biến môi trường ở process backend và gateway.
- `400 mission-to-UAV`: sửa source/UAV mapping hoặc UAV được gán cho mission; không đổi payload để lách mapping.
- Không có data: kiểm tra gateway target dùng LAN IP, firewall TCP 5000, backend bind `0.0.0.0`, rồi mới kiểm tra FE.
- Gateway không mở được COM: Mission Planner/process khác đang giữ port; dùng UDP forwarding hoặc đóng process đó.
- SSE mở nhưng không đổi: kiểm tra ingest response/DB/latest trước, sau đó proxy buffering (`X-Accel-Buffering: no`) và network timeout.
- Sensor không có: field phải là `null`; UI hiển thị unavailable/no data, không hiển thị 0.

## 8. Mẫu biên bản

```text
Commit:
UTC start/end:
Runner: PASS/FAIL (x/26)
Source: fixture | PX4 hardware
PX4 firmware:
Transport: COM/baud | UDP bind/target
Gateway version/commit:
Backend URL / Frontend URL:
Mission ID / UAV ID / Source ID:
Latest + SSE + reconnect latency:
Observed Online → Stale → Offline → Online:
Blockers / linked tasks:
Secrets removed from evidence: YES/NO
Hardware verified: YES/NO
```
