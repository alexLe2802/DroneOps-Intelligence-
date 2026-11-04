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

Envelope thực thi thêm `commandId`, owner, controller UID, connection/lease, `compiledMissionHash`, profile/adapter version, `issuedAtUtc`, `scheduledAtUtc` và `expiresAtUtc`. Credential gateway không nằm trong dữ liệu trả về browser.

Gateway lưu receipt/outcome bền vững trước tác động vật lý. Backend cấp một dispatch identity cho một approved execution snapshot. Gửi lại tối đa ba lần cùng identity trước deadline chỉ là retry giao nhận; không tạo thêm lần start.

Giới hạn transfer của release: chờ phản hồi 3 giây, tối đa 3 retry mỗi bước và tối đa 120 giây cho toàn bộ upload/read-back. Profile có phiên bản và audit có thể đặt giới hạn chặt hơn.

## Thay đổi và sự cố

- Đổi kế hoạch, connection, UID hoặc mission onboard làm mất hiệu lực binding cũ; phải xác minh và xác nhận lại.
- Cancel trước dispatch thu hồi consent/authorization. Cancel kế hoạch không phải lệnh dừng drone đang bay.
- Thiếu điều kiện hoặc quá deadline: hiển thị lý do Blocked/Expired; không tự bay bù khi mạng trở lại.
- ACK thất lạc hoặc restart giữa lúc start: đối chiếu trạng thái thực; không tự ARM/start lại khi outcome chưa rõ.
- Mất web/backend sau takeoff: autopilot xử lý theo failsafe đã cấu hình; pilot có RC takeover. Không mặc định mọi tình huống đều RTL.
- Photo action cần camera capability; profile/frame/action không hỗ trợ phải bị chặn trước dispatch.

## Hồ sơ kiểm chứng

SRS quy định FR-AF-01–FR-AF-12 và UC-AF-01–UC-AF-03. Workbook chức năng ghi AF-01–AF-16; workbook unit ghi UT-AF-01–UT-AF-07. Kết quả và bằng chứng từng lần chạy được ghi độc lập với trạng thái “bản cuối” của tài liệu. SITL, bench tháo cánh và bay giám sát là các mức bằng chứng riêng.
