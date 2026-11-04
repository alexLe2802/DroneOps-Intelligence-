# Vận hành nhiệm vụ bay tự động

Tài liệu vận hành theo đặc tả chức năng cuối cùng của DroneOps Intelligence. Phạm vi gồm kế hoạch đã duyệt, gateway tin cậy, biên dịch và upload mission, xác nhận pilot, thực thi theo lịch, giám sát và kết thúc chuyến bay.

## Vai trò và phương tiện

- MF1 — NghiaHQ: kế hoạch, duyệt, validation và preflight.
- MF2 — ThongLQ: gateway, thực thi tự động theo lịch, realtime và incidents.
- MF3 — DucDDA: execution history, kết thúc chuyến bay và báo cáo.
- MF4 — KhanhGNG: AI hỗ trợ đánh giá; AI không duyệt hoặc cho phép cất cánh.

Gateway chạy trên Jetson hoặc laptop nối radio. Profile thực thi hỗ trợ ArduPilot Copter và PX4 multicopter. Autopilot giữ vòng điều khiển ổn định, kiểm tra pre-arm và failsafe onboard. Fixed-wing/VTOL, phát setpoint Guided/Offboard liên tục và tự phát triển thuật toán ổn định nằm ngoài phạm vi profile này.

## Quy trình vận hành

1. Operator chọn UAV, tuyến waypoint, giờ UTC, tốc độ, độ cao/reference và `AutomaticFlightRequested`. Mặc định lựa chọn tắt; mọi thay đổi đi qua proposal/approval.
2. Manager duyệt đúng nội dung và validation. Mission có pending update không đủ điều kiện dispatch.
3. Gateway xác minh UID flight controller, firmware, airframe, target IDs và capability. Biên dịch kế hoạch theo profile; lưu `compiledMissionHash` riêng với `planHash`.
4. Upload qua MAVLink Mission Protocol, nhận kết quả và download/read-back để so nội dung đã chuẩn hóa. Chỉ ACK upload chưa chứng minh drone đã bay. [MAVLink Mission Protocol](https://mavlink.io/en/services/mission.html).
5. Trong khoảng **T−5 phút ≤ backend now < giờ bay**, pilot mở tab đã đăng nhập, xem đúng mission/UAV và xác nhận. Consent gắn với plan hash, connection và UID; thao tác xác nhận chưa ARM hoặc cất cánh.
6. Đúng giờ bay, scheduler kiểm tra lại toàn bộ điều kiện và phát một envelope có `commandId` duy nhất, giới hạn thời gian. Gateway kiểm tra điều kiện cục bộ và thực thi theo profile autopilot.
7. Theo dõi ACK cùng trạng thái armed/in-air và mission progress. Chỉ bằng chứng bay thực từ telemetry mới tạo execution snapshot và chuyển mission thành `InProgress`.
8. Khi có bằng chứng landed/terminal hợp lệ, hệ thống kết thúc execution và tạo báo cáo một lần. Chuyến bay bị gián đoạn vẫn giữ outcome và coverage thực tế.

Autopilot thực thi mission onboard; cơ chế bắt đầu và hành vi theo firmware được ràng buộc bởi profile đã xác minh. Xem [ArduPilot AUTO](https://ardupilot.org/copter/docs/auto-mode.html) và [PX4 Mission](https://docs.px4.io/main/en/flight_modes_mc/mission).

## Điều kiện dispatch

Tất cả điều kiện phải đồng thời hợp lệ:

| Nhóm | Điều kiện |
| --- | --- |
| Kế hoạch | Approved, opt-in, đúng schedule/reservation, không pending update |
| Người vận hành | Eligible pilot, consent trong cửa sổ; presence không quá 30 giây |
| Gateway | Trusted credential còn hiệu lực, đúng UAV/UID, lease/connection chưa hết hạn hoặc bị thu hồi |
| Kết nối | Heartbeat không quá 30 giây; telemetry thực không quá 15 giây |
| Validation | Preflight Pass đúng plan hash trong 60 giây gần nhất, được kiểm tra lại khi dispatch |
| Mission onboard | Đúng compiled hash/profile; upload và normalized read-back đã xác minh |
| Điều kiện cục bộ | Capability, home/frame/terrain, pre-arm và failsafe phù hợp profile |
| Thời gian | Không sớm hơn giờ bay; envelope hết hạn tại giờ bay + 5 giây; sai số đồng hồ backend/gateway tối đa 1 giây |

Portal gửi presence mỗi 10 giây khi hiển thị và online. `dispatchEnabled` là kết quả của cấu hình release và toàn bộ gate; giá trị không được cố định để bỏ qua hoặc vô hiệu hóa quy trình trong đặc tả.

## Hợp đồng dữ liệu

Plan export gồm schema version, mission/UAV IDs, `planHash`, lịch UTC, tốc độ m/s, altitude reference và waypoint theo thứ tự. Tọa độ WGS84 có dạng độ và E7 (độ × 10^7); độ cao dùng mét theo reference. Không đổi giá trị độ cao thiếu thành 0 hoặc đổi AGL sang Relative bằng cách đổi tên.

`GET /api/missions/{id}/automation/plan` trả thêm `compilationPreviews` cho ArduPilot/PX4. `MissionCommandCompiler` kiểm tra input và dựng candidate `MISSION_ITEM_INT`; dữ liệu hoặc policy chưa đầy đủ trả `NeedsInput`. Preview chỉ thể hiện khả năng encode: `CanEncode` không cấp quyền `CanUpload`/`CanStart`, và không thay thế approval, consent, upload/read-back hoặc dispatch gate.

Theo hợp đồng compiler hiện hành, MSL dùng `MAV_FRAME_GLOBAL` (0), Relative dùng `MAV_FRAME_GLOBAL_RELATIVE_ALT` (3); `MISSION_ITEM_INT.x/y` vẫn là int32 E7. Không suy ra kiểu encode từ hậu tố tên frame hoặc tự đổi AGL sang Relative. Chi tiết wire fields, home/landing altitude, policy và trạng thái compiler nằm trong [dữ liệu thực thi và failsafe](automatic-flight-data-and-failsafe-review.md).

Envelope thực thi thêm `commandId`, owner, controller UID, connection/lease, `compiledMissionHash`, profile/adapter version, `issuedAtUtc`, `scheduledAtUtc` và `expiresAtUtc`. Credential gateway không nằm trong dữ liệu trả về browser.

Gateway lưu receipt/outcome bền vững trước tác động vật lý. Backend cấp một dispatch identity cho một approved execution snapshot. Gửi lại tối đa ba lần cùng identity trước deadline chỉ là retry giao nhận; không tạo thêm lần start.

Giới hạn transfer của release: chờ phản hồi 3 giây, tối đa 3 retry mỗi bước và tối đa 120 giây cho toàn bộ upload/read-back. Profile có phiên bản và audit có thể đặt giới hạn chặt hơn.

## Thay đổi và sự cố

- Đổi kế hoạch, connection, UID hoặc mission onboard làm mất hiệu lực binding cũ; phải xác minh và xác nhận lại.
- Cancel trước dispatch thu hồi consent/authorization. Cancel kế hoạch không phải lệnh dừng drone đang bay.
- Thiếu điều kiện hoặc quá deadline: hiển thị lý do Blocked/Expired; không tự bay bù khi mạng trở lại.
- ACK thất lạc hoặc restart giữa lúc start: đối chiếu trạng thái thực; không tự ARM/start lại khi outcome chưa rõ.
- Mất web/backend sau takeoff: autopilot xử lý theo failsafe đã cấu hình; pilot có RC takeover. Không mặc định mọi tình huống đều RTL.
- Profile tạo/sửa mission hiện hành dùng TakeOff, FlyThrough, Hover và Land. Camera/CapturePhoto không được đưa vào mission mới; dữ liệu lịch sử vẫn được giữ. Action hoặc frame không hỗ trợ phải bị chặn trước dispatch.

## Hồ sơ kiểm chứng

SRS quy định FR-AF-01–FR-AF-12 và UC-AF-01–UC-AF-03. Workbook chức năng ghi AF-01–AF-16; workbook unit ghi UT-AF-01–UT-AF-07. Kết quả và bằng chứng từng lần chạy được ghi độc lập với trạng thái “bản cuối” của tài liệu. SITL, bench tháo cánh và bay giám sát là các mức bằng chứng riêng.

## Live Monitoring và nhắc chuẩn bị

Live Monitoring chứa form kết nối/pairing và phần xác nhận; presence theo dõi tab portal đang hiển thị. Dùng lại dữ liệu mission, connection và notification hiện có, không tạo nguồn dữ liệu flight hoặc API kết nối trùng chức năng.

- Từ T−5 đến trước `EndTime`, mission Approved có opt-in được theo dõi chuẩn bị, kể cả gateway kết nối trễ. Readiness ghi nhận sau StartTime không cho phép tự bay bù hoặc vượt deadline dispatch của kế hoạch.
- Chưa có gateway đúng UAV/UID: gửi `FlightConnectionRequired` một lần trong vòng đời mission, kể cả sau khi thông báo đã đọc, reconnect hoặc worker restart.
- Gateway online nhưng telemetry chưa mới hoặc preflight chưa Pass: tiếp tục chờ; chưa gửi thông báo có thể xác nhận.
- Telemetry mới và preflight Pass đúng plan hash: gửi `FlightConfirmationRequired` khi chưa có consent hợp lệ. Kiểm tra ngay sau telemetry WebSocket đầu tiên và mỗi 10 giây trong worker preflight để bao phủ REST gateway và dữ liệu đến trễ.
- Chưa xác nhận: nhắc lại sau ít nhất 2 phút từ lần nhắc confirm gần nhất. Đọc thông báo không phải confirm. Consent đúng plan/connection dừng nhắc; cache preflight hết hạn không tự xóa bản ghi consent, nhưng readiness/dispatch vẫn cần preflight mới và Pass.
- Trước mỗi lần gửi, kiểm tra lại status và EndTime. Khóa row Mission cùng transaction ghi Notification để chống trùng giữa worker/API instance. Sau EndTime hoặc khi mission Completed, Cancelled, Aborted, Failed hay InProgress thì dừng nhắc; giữ lịch sử đã gửi.
- Notification dùng kênh trong web hiện có, action mở `/portal/live-monitoring?missionId=...`; không tự thêm email, SMS hoặc push.

### Hợp đồng API chuẩn bị

| API | Nội dung tích hợp |
| --- | --- |
| `GET /api/missions/automation/due` | Bao phủ từ T−5 đến trước EndTime, gồm kết nối trễ; trả `endTime`, không giới hạn 20 mission làm sót lịch |
| `GET /api/missions/{id}/automation`, `POST .../presence` | `endsAt`, `gatewayConnected`, `preflightReady`, `confirmationRecorded`; tách gateway online khỏi telemetry mới |
| `POST .../confirm` | Readiness được ghi nhận trước EndTime sau kiểm tra owner/hash/preflight/presence/UID; ghi nhận trễ không tạo lại quyền dispatch đã hết hạn |
| `POST .../cancel` | Thu hồi consent trong Live Monitoring |
| Notification list/count/read/read-all | Giữ endpoint/response, dùng thêm hai notification type ở trên |
| WebSocket `/ws/drone-gateway` | Giữ telemetry payload/ACK; lỗi gửi nhắc không được ngắt ingestion |

Các phần nhắc và UI chuẩn bị dùng Mission, DroneConnection, MissionValidationRun, MissionAutomationState và Notification hiện có; không đòi thêm migration chỉ cho cơ chế nhắc.

## Hiển thị giờ Việt Nam

Giờ lịch nhập trên web dùng `Asia/Ho_Chi_Minh`/GMT+7 dù browser ở múi giờ khác. Nhập 05/11/2026 01:30 tương ứng API `2026-11-04T18:30:00Z`; mở form sửa vẫn hiển thị 05/11/2026 01:30. API, database và packet gateway giữ UTC/offset ISO; không cộng 7 giờ vào dữ liệu lưu. T−5, 2 phút và EndTime so sánh cùng một instant.

Notification, Live Monitoring, dashboard, duyệt nhiệm vụ, incident và lịch sử hiển thị giờ Việt Nam. Ngày sinh và ngày giấy phép là calendar date, không dịch ngày theo múi giờ.

Xem thêm [pin và telemetry](battery-telemetry-and-assessment.md), [hợp đồng dữ liệu mission](examples/droneops-mission-fields.md) và các file mẫu `.plan`/`.waypoints` trong `docs/examples/`.


## Executor runtime integration

Runtime executor remains experimental: backend `FlightExecution:Enabled=false`; gateway flight control is opt-in via `--enable-flight-control`. Preparation API retains `dispatchEnabled=false`. The manual ARM flow below has its own timing contract, separate from the scheduler specification above.

## ARM do pilot quyết định và đếm ngược 10 giây

Luồng đã được người dùng chọn: kết nối FC → lưu thông số biên dịch còn thiếu →
tự upload/read-back khi FC disarmed, trên mặt đất → xác nhận tại Live Monitoring
→ **chỉ từ StartTime** mới cho bấm ARM → COMMAND_ACK và heartbeat xác nhận ARM
→ backend đặt deadline sau 10 giây → gateway cũng kiểm tra đủ 10 giây bằng đồng
hồ monotonic → gửi MISSION_START. Mission đã có TAKEOFF nên không phát thêm
GUIDED TAKEOFF. ARM từ RC/GCS không tự tạo countdown.
Gateway yêu cầu UTC đồng bộ với backend (lệch tối đa 2 giây); bỏ lệnh trễ từ
3 giây hoặc đã hết deadline. Đồng hồ monotonic cục bộ bảo đảm đủ 10 giây,
không phụ thuộc việc trình duyệt đếm nhanh/chậm.

Live Monitoring thay lời nhắc DISARM bằng thanh trạng thái/đếm ngược. Nút DISARM
khả dụng khi FC xác nhận đang armed và ON_GROUND; không force-disarm, không đưa
vào nút thông báo. Sau khi đã phát Start, web không cung cấp DISARM để tránh
giá trị landed còn trễ làm tắt động cơ ngay khi cất cánh. Dùng RC/GCS để điều
khiển phục hồi và đáp; chức năng disarm sau bay cần đối soát vòng đời riêng.

Pilot presence điều khiển cập nhật mỗi giây, lease 3 giây (độc lập presence nhắc
chuẩn bị 10/30 giây). Mất readiness, consent, FC ground state, navigation, mạng,
tab online hoặc hết EndTime sẽ hủy countdown. Đóng/ẩn tab gửi cancel tốt nhất
có thể; lease backend/gateway giới hạn độ trễ hủy khi gói cancel mất. Hủy không
tự DISARM. Kết nối lại/reboot không tiếp tục countdown. Kết quả lệnh không rõ
được lưu Unknown, không tự retry. UI về 0 chỉ hiển thị chờ FC, không giả lập bay.

Thông số FC được lưu gồm firmware, multicopter, acceptance radius, yaw policy,
yaw angle nếu Fixed, thời gian Hover và cao độ mặt đất điểm đáp theo frame của
mission. Không tự lấy độ cao đường bay làm độ cao mặt đất. Mỗi lần lưu sẽ thu
hồi consent, yêu cầu xác nhận lại. Profile thử nghiệm giới hạn 500 compiled
items (ArduPilot thêm home ở seq 0). ArduPilot yêu cầu Loiter trước ARM; PX4 yêu
cầu Position mode để tránh ARM trong AUTO làm bay trước countdown.

API mở rộng trong controller có sẵn, cùng ownership/role và CSRF BFF:

| Endpoint | Công dụng |
| --- | --- |
| `POST /api/missions/{id}/automation/plan` | `{planHash, options}` lưu input compiler; giữ nguyên GET preview |
| `GET /api/missions/{id}/automation/execution` | Đọc trạng thái FC, upload, countdown và khả năng thao tác |
| `POST /api/missions/{id}/automation/execution` | Đọc trạng thái kèm presence của tab monitoring |
| `POST /api/missions/{id}/automation/arm` | `{operationId, compiledHash}` yêu cầu ARM đúng plan từ StartTime |
| `POST /api/missions/{id}/automation/disarm` | Cùng body; DISARM mặt đất do pilot quyết định |
| `POST /api/missions/{id}/automation/cancel` | Tái sử dụng endpoint cũ; thu hồi consent và hủy countdown trước Start |
| `/ws/drone-gateway`, `controlPoll`/`controlAck` | Tái sử dụng token/UID/connection; evidence FC và command receipt |

`MissionAutomationState.ExecutionJson` lưu transition, deadline, gateway
instance, compiled hash và tối đa 32 command receipts gần nhất; không phải
immutable audit log dài hạn. Migration `20261104160247_manual_arm_countdown.sql`
không mở Data API cho anon/authenticated. Không tạo API thông báo/kết nối/mission
trùng chức năng hiện có.

**Chưa đủ điều kiện bật bay thật:** chưa có SITL ArduPilot/PX4 hoặc FC vật lý
để xác nhận adapter read-back (bao gồm chuẩn hóa home/yaw/unused params), firmware
version được hỗ trợ, mission-current reset, chống GCS khác sửa mission, pre-arm
health theo firmware và battery/failsafe policy. Cần thêm gate pin đủ nhiệm vụ
theo calculator đã có, kiểm tra FC failsafe parameters thực tế, kiểm thử GCS/RC
loss, EKF/GPS/fence và ngân sách thời gian trước EndTime. Các policy trong tài
liệu review vẫn chờ duyệt. Không bật hai cờ điều khiển trên production chỉ vì
unit/integration test đã pass. Không đổi tham số FC hoặc service Jetson ở bước này.
