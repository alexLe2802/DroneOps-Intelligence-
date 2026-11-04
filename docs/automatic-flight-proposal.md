# Chuẩn bị bay tự động — ArduPilot và PX4

## Phạm vi bản thay đổi này

- Mission có lựa chọn `AutomaticFlightRequested`, mặc định tắt. Thay đổi lựa chọn
  đi qua quy trình đề xuất/duyệt kế hoạch; không sửa ngầm một mission đã duyệt.
- Live Monitoring nhận xác nhận trong khoảng **T−5 phút ≤ hiện tại < EndTime**,
  tính bằng đồng hồ backend. Có thể xác nhận khi kết nối trễ sau StartTime;
  không tự hiểu kết nối trễ là đồng ý bay. Tab phải hiển thị, có Internet và
  phiên đăng nhập còn hợp lệ. Mission phải còn Approved và bật chuẩn bị tự động.
- Presence gửi mỗi 10 giây, hết hiệu lực sau 30 giây. Gateway phải có heartbeat
  trong 30 giây và telemetry thực trong 15 giây; gateway online đơn thuần chưa đủ.
- Xác nhận chạy lại preflight, gắn với hash của kế hoạch và connection của đúng
  UAV/UID. Đổi kế hoạch/connection phải xác nhận lại. Bỏ qua, đóng web hoặc quá
  EndTime không được hiểu là đồng ý bay. Hết EndTime hoặc mission đã Completed/
  Cancelled/Aborted/Failed/InProgress thì không gửi thêm nhắc chuẩn bị.
- `GET /api/missions/{id}/automation/plan` xuất dữ liệu kế hoạch cho chủ mission.
- **Chưa có uploader/ARM/TAKEOFF/AUTO/MISSION_START xuống flight controller.**
  `dispatchEnabled=false` được trả rõ ràng. Xác nhận chỉ lưu readiness, không
  đổi mission thành đang bay và không tác động động cơ.

Đây là phần chuẩn bị cho luồng điều khiển, không phải bằng chứng đã bay tự động
trên drone thật. Gateway vẫn chỉ yêu cầu telemetry/UID và chuyển dữ liệu lên web.

## Nhắc kết nối và nhắc xác nhận đã triển khai

- Từ T−5 đến trước EndTime: nếu chưa có gateway của đúng UAV/UID kết nối,
  ghi một thông báo `FlightConnectionRequired` cho chủ mission. Chỉ một lần
  trong vòng đời mission, kể cả đã đọc, mất kết nối lại hoặc worker khởi động lại.
- Gateway online nhưng telemetry chưa mới hoặc preflight chưa Pass thì chờ dữ
  liệu, không gửi thông báo rằng có thể xác nhận.
- Khi telemetry mới và preflight Pass cho đúng planHash: gửi
  `FlightConfirmationRequired` nếu chưa có xác nhận còn hiệu lực. Lần đầu nhận
  telemetry qua WebSocket kiểm tra ngay; worker preflight hiện có cũng kiểm tra
  mỗi 10 giây để bao phủ REST gateway, kết nối trễ và dữ liệu hoàn tất sau đó.
- Nếu chưa xác nhận, nhắc tiếp khi đã qua ít nhất 2 phút từ thông báo confirm
  gần nhất. Đã đọc thông báo không đồng nghĩa đã confirm. Đã xác nhận đúng kế
  hoạch/connection thì dừng nhắc; cache preflight hết hạn không tự xóa sự đồng ý.
  Preflight vẫn phải mới và Pass để trạng thái readiness được công nhận.
- Mỗi lần gửi kiểm tra lại status và EndTime. Khóa row Mission cùng transaction
  với ghi Notification chống gửi trùng giữa nhiều worker/API instance. Lịch sử
  thông báo đã gửi được giữ lại sau EndTime; không phát thêm thông báo mới.
- Dùng lại `NotificationService`, bảng Notification và notification center hiện
  có (thông báo trong web, không tự thêm kênh email/SMS/push). Action mở
  `/portal/live-monitoring?missionId=...`. Form kết nối/pairing hiện có được nhúng
  vào Live Monitoring; không tạo bản API kết nối hoặc nguồn dữ liệu flight mới.
- Phần hỏi xác nhận toàn portal được chuyển vào Live Monitoring. Presence vẫn
  theo dõi tab portal đang hiển thị. Một tab đang online không có nghĩa đồng ý bay.

## API thay đổi trong bản bổ sung

Không tạo endpoint mới và không tạo migration mới. Dùng các field Mission,
DroneConnection, MissionValidationRun, MissionAutomationState và Notification có sẵn.

| API hiện có | Thay đổi |
| --- | --- |
| `GET /api/missions/automation/due` | Trả cả mission kết nối trễ, từ T−5 đến trước EndTime; thêm `endTime`, bỏ giới hạn 20 làm sót mission |
| `GET /api/missions/{id}/automation` và `POST .../presence` | Thêm `endsAt`, `gatewayConnected`, `preflightReady`, `confirmationRecorded`; phân biệt gateway online với telemetry mới |
| `POST /api/missions/{id}/automation/confirm` | Cho phép confirm muộn trước EndTime, giữ kiểm tra owner/hash/preflight/presence/UID và khóa chống race với nhắc lịch |
| `POST .../cancel` | Giữ contract; thao tác thu hồi xác nhận nằm trong Live Monitoring |
| `GET /api/notifications`, unread-count, read/read-all | Giữ endpoint và response; thêm hai giá trị type nêu trên qua service gửi sẵn có |
| WebSocket `/ws/drone-gateway` | Giữ payload/ACK; sau telemetry đầu tiên kiểm tra nhắc confirm, lỗi thông báo không ngắt telemetry |
| Mission create/update, live missions/telemetry, automation/plan | Giữ contract và nguồn dữ liệu; không tạo API trùng chức năng |

## Giờ Việt Nam GMT+7

Giờ lịch nhập trên web luôn là `Asia/Ho_Chi_Minh`/GMT+7, dù browser ở múi giờ
khác. Ví dụ nhập 05/11/2026 01:30 sẽ gửi API `2026-11-04T18:30:00Z`; mở lại
form sửa vẫn là 05/11/2026 01:30. API/database và packet gateway giữ UTC/offset
ISO để không làm lệch dữ liệu cũ. Các mốc T−5/2 phút/EndTime so sánh cùng instant,
không cộng thêm 7 giờ vào thời gian lưu. Notification, Live Monitoring, dashboard,
duyệt nhiệm vụ, incident và lịch sử hiển thị giờ Việt Nam. Trường ngày sinh/ngày
giấy phép là calendar date, không đổi ngày do dịch múi giờ.

## Dữ liệu đang có trong hệ thống

| Trường | Đơn vị/ý nghĩa | Đưa xuống drone |
| --- | --- | --- |
| `missionId`, `uavId`, `planHash` | Định danh và phiên bản kế hoạch | Envelope của gateway; không phải tọa độ MAVLink |
| `scheduledStartUtc`, `scheduledEndUtc` | Giờ UTC | Scheduler kiểm soát; upload waypoint không tự hẹn giờ cất cánh |
| `averageSpeedMetersPerSecond` | m/s | Tốc độ hành trình mong muốn; không dùng thời gian bay ước tính thay tốc độ điều khiển |
| `maxSpeedMetersPerSecond` | m/s | Giới hạn kế hoạch; cần kiểm tra giới hạn phương tiện, không tự ghi một parameter chung cho mọi firmware |
| `altitudeReference` | Relative / MSL / AGL | Chọn frame phù hợp sau khi xác minh home/terrain |
| `waypoints[].sequenceOrder` | Thứ tự trên web, bắt đầu 1 | Adapter dựng `seq` MAVLink bắt đầu 0; xử lý home item theo firmware |
| `latitudeDegrees`, `longitudeDegrees` | WGS84, độ | `MISSION_ITEM_INT.x/y` = độ × 10^7, int32 |
| `latitudeE7`, `longitudeE7` | Giá trị đã chuẩn hóa × 10^7 | Chỉ tọa độ; không nhầm với mét |
| `altitudeMeters` | Mét theo altitude reference | `MISSION_ITEM_INT.z`; không tự thay null thành 0 |
| `action` | TakeOff / FlyThrough / Hover / Land | Biên dịch sang command được firmware và phần cứng hỗ trợ |

Trước khi gửi, gateway cần thêm `hardwareIdentity`, system/component ID được phát
hiện thực tế, autopilot/firmware version, vehicle type, MAVLink capabilities,
`commandId`, thời hạn lệnh, plan revision/hash và connection/lease hiện tại.
UID nhận từ MAVLink là định danh của flight controller, không phải chứng thực
phần cứng chống giả mạo. Credential gateway vẫn phải được giữ kín và có thể thu hồi.

Ví dụ dữ liệu kế hoạch (đây không phải gói MAVLink đã sẵn sàng thực thi):

```json
{
  "schemaVersion": 1,
  "missionId": "<uuid>",
  "uavId": "<uuid>",
  "planHash": "<sha256>",
  "dispatchEnabled": false,
  "scheduledStartUtc": "2026-11-05T02:00:00Z",
  "altitudeReference": "Relative",
  "averageSpeedMetersPerSecond": 5,
  "maxSpeedMetersPerSecond": 8,
  "waypoints": [
    {"sequenceOrder": 1, "latitudeDegrees": 10.8231, "longitudeDegrees": 106.6297, "latitudeE7": 108231000, "longitudeE7": 1066297000, "altitudeMeters": 50, "action": "TakeOff"},
    {"sequenceOrder": 2, "latitudeDegrees": 10.8240, "longitudeDegrees": 106.6305, "latitudeE7": 108240000, "longitudeE7": 1066305000, "altitudeMeters": 50, "action": "FlyThrough"},
    {"sequenceOrder": 3, "latitudeDegrees": 10.8231, "longitudeDegrees": 106.6297, "latitudeE7": 108231000, "longitudeE7": 1066297000, "altitudeMeters": 0, "action": "Land"}
  ]
}
```

## Các trường còn phải bổ sung trước khi biên dịch mission

| Nhóm | Trường cần chốt |
| --- | --- |
| Phương tiện | ArduPilot/PX4, phiên bản, multicopter/fixed-wing/VTOL, capability flags |
| Tọa độ/độ cao | frame, home lat/lon/alt MSL, takeoff altitude, terrain source và độ mới dữ liệu |
| Waypoint | acceptance radius (m), pass radius (m), yaw (độ hoặc giữ heading), hold time (s), autocontinue |
| Tốc độ | ground speed cho copter, airspeed/groundspeed policy cho fixed-wing; lên/xuống m/s và giới hạn tăng tốc |
| Hover | thời gian dừng; với fixed-wing cần loiter radius/direction thay vì đứng yên |
| Hạ cánh/VTOL | điểm tiếp cận, approach altitude/slope, VTOL transition, landing policy; không dùng chung một chuỗi lệnh cho mọi airframe |
| Failsafe | pin thấp, mất RC, mất telemetry/GCS, mất GPS/EKF, geofence, RTL altitude và điểm đáp dự phòng |

`MSL` có thể dùng `MAV_FRAME_GLOBAL_INT`, `Relative` dùng
`MAV_FRAME_GLOBAL_RELATIVE_ALT_INT`. `AGL` chỉ dùng terrain frame khi firmware và
terrain thực sự hỗ trợ; không đổi AGL thành relative bằng cách thay tên field.

Các command ứng viên: `MAV_CMD_NAV_TAKEOFF` (22), `NAV_WAYPOINT` (16),
`NAV_LOITER_TIME` (19), `NAV_LAND` (21), `DO_CHANGE_SPEED` (178). Mỗi `MISSION_ITEM_INT` gồm target IDs, `seq`, `frame`,
`command`, `current`, `autocontinue`, `param1..4`, `x`, `y`, `z`, `mission_type`.
Ý nghĩa param phụ thuộc command và có khác biệt hỗ trợ theo firmware.

## Phương án

| Phương án | Jetson | Radio + laptop | Đánh giá |
| --- | --- | --- | --- |
| Upload toàn bộ mission vào autopilot; autopilot chạy onboard | Gateway trên Jetson upload qua MAVLink | Cùng gateway upload qua radio | **Ưu tiên**. Hai đường truyền dùng cùng contract; autopilot giữ mission và failsafe |
| Điều khiển Guided/Offboard liên tục từ companion | Jetson phát setpoint cục bộ, web chỉ gửi mục tiêu cấp cao | Phụ thuộc laptop/radio liên tục | Phù hợp tránh vật cản/vision; cần watchdog và adapter riêng. Không dùng Internet để bảo đảm nhịp setpoint |
| Xuất kế hoạch và vận hành bằng Mission Planner/QGC | Dùng GCS để kiểm tra/upload | Tự nhiên với laptop | Bước đối chiếu và dự phòng trước khi bật auto-launch trên web |

Luồng ưu tiên:

1. Sau duyệt, phát hiện firmware/airframe/capabilities và biên dịch mission.
2. Upload bằng Mission Protocol (`MISSION_COUNT` → `MISSION_REQUEST_INT` /
   `MISSION_ITEM_INT` → `MISSION_ACK`), có timeout, retry giới hạn, chống lặp.
3. Giữ riêng `planHash` của kế hoạch đã duyệt và `compiledMissionHash` của lệnh
   biên dịch theo firmware/adapter version. Download/read-back và so hash của bản
   MAVLink đã chuẩn hóa (float32, NaN, frame, thứ tự/home item). ACK nhận đủ gói chưa phải
   bằng chứng drone đã cất cánh.
4. T−5 phút: hiển thị đúng mission/drone, pin/GPS/vị trí/khí tượng/địa hình và
   hỏi pilot. Không ARM ngay khi pilot bấm xác nhận.
5. Đúng giờ bay: scheduler kiểm tra lại approval/hash, presence, UID, telemetry,
   mission upload/read-back, pre-arm và vùng cất cánh. Thiếu điều kiện thì **Blocked**;
   không tự bay bù khi Internet trở lại sau giờ hẹn.
6. Gửi envelope một lần với `commandId`, deadline và plan hash. Gateway chống
   replay và kiểm tra điều kiện cục bộ. Adapter ArduPilot/PX4 chọn mode/start/arm
   đúng loại phương tiện; kiểm tra `COMMAND_ACK` và trạng thái thực từ heartbeat,
   landed state, altitude, mission progress trước khi đánh dấu `InProgress`.
7. Mất web sau cất cánh: thực thi failsafe onboard đã được pilot cấu hình và thử.
   Không hứa luôn RTL; lựa chọn phụ thuộc airframe, vị trí và tình huống.

Thứ tự kiểm thử: ArduPilot SITL Copter + PX4 SITL multicopter → bench tháo cánh →
bay có người giám sát và RC override. Fixed-wing/VTOL là profile riêng, không suy
ra khả năng từ việc board trả UID hoặc telemetry.

Nguồn chính thức:
- https://mavlink.io/en/services/mission.html
- https://mavlink.io/en/messages/common.html#MISSION_ITEM_INT
- https://ardupilot.org/copter/docs/auto-mode.html
- https://docs.px4.io/main/en/flight_modes_mc/mission.html
- https://docs.px4.io/main/en/flight_modes/offboard.html

Camera/CapturePhoto được hoãn khỏi luồng tạo/sửa nhiệm vụ hiện tại. Dữ liệu lịch
sử vẫn được giữ; không đưa camera vào yêu cầu dữ liệu cho adapter ở giai đoạn này.
