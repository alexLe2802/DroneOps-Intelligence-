# File mission mẫu và dữ liệu DroneOps còn thiếu

## Các file

- `droneops-multicopter.example.plan`: JSON theo cấu trúc Plan của QGroundControl,
  với `groundStation=DroneOps`. Chọn ArduPilot (`firmwareType=3`) và quadcopter
  (`vehicleType=2`) cho ví dụ này.
- `droneops-multicopter.example.waypoints`: cùng các hành động ở dạng QGC WPL 110
  để đối chiếu với chức năng Load WP File của Mission Planner. Dòng đầu index 0
  là planned home trong file minh họa; không coi đó là điểm bay đầu tiên.

Đây là dữ liệu minh họa thiết kế, chưa được kiểm thử import trong GCS, SITL hoặc
bay thật. Tọa độ và home altitude 12 m là số ví dụ, không lấy từ drone của bạn.
DroneOps hiện chưa có exporter/uploader thực thi các file này; endpoint plan hiện
có vẫn trả dữ liệu kế hoạch cấp ứng dụng, không phải định dạng `.plan` này.

`.plan` là file của ground station. Upload tới FC cần biên dịch thành mission
items qua MAVLink Mission Protocol; không gửi nguyên file JSON để FC tự đọc.
Xem [QGC Plan format](https://docs.qgroundcontrol.com/master/en/qgc-dev-guide/file_formats/plan.html),
[MAVLink mission upload](https://mavlink.io/en/services/mission.html#upload-a-mission-to-the-vehicle),
[WPL format](https://mavlink.io/en/file_formats/),
[Mission Planner load/save](https://ardupilot.org/planner/docs/common-planning-a-mission-with-waypoints-and-events.html).

## Kịch bản của mẫu

| Item | Command | Ý nghĩa |
| --- | --- | --- |
| 1 | 22 / NAV_TAKEOFF | Cất cánh đến 30 m so với home |
| 2 | 178 / DO_CHANGE_SPEED | Đặt ground speed 5 m/s; không thay throttle |
| 3 | 16 / NAV_WAYPOINT | Bay đến điểm A, bán kính chấp nhận 2 m |
| 4 | 16 / NAV_WAYPOINT | Bay đến điểm B, giữ 5 giây cho multicopter |
| 5 | 21 / NAV_LAND | Hạ cánh tại tọa độ home minh họa, ground level relative 0 m |

Yaw 90° trong mẫu là lựa chọn minh họa, không phải giá trị có sẵn trên form
DroneOps. `frame=3` nghĩa độ cao relative-to-home, không phải AGL theo địa hình.
Home MSL 12 m + relative 30 m cho mức MSL dự kiến 42 m; cần đối chiếu home thật.
`plannedHomePosition` chỉ là home phục vụ lập kế hoạch, không tự đặt HOME_POSITION
trên FC. `frame=2` của item tốc độ không chứa tọa độ điều hướng.

`params` có 7 phần tử và phụ thuộc từng command. Với **NAV_WAYPOINT (16)**:

| Index JSON | MAVLink | Giá trị điểm B | Ý nghĩa |
| --- | --- | --- | --- |
| 0 | param1 | 5 | Hold time, giây |
| 1 | param2 | 2 | Acceptance radius, mét |
| 2 | param3 | 0 | Pass radius, mét |
| 3 | param4 | 90 | Yaw, độ |
| 4 | x | 10.8234 | Latitude, độ WGS84 trong file |
| 5 | y | 106.6300 | Longitude, độ WGS84 trong file |
| 6 | z | 30 | Độ cao, mét theo frame |

Không áp dụng bảng param này cho TAKEOFF/LAND/CHANGE_SPEED. Ví dụ item tốc độ
`[1,5,-1,0,0,0,0]`: speed type ground speed, speed 5 m/s, throttle giữ nguyên.
Khi dùng MISSION_ITEM_INT, tọa độ global được chuyển từ độ sang int32 × 10^7;
không ghi tọa độ E7 vào trường params lat/lon của file `.plan`.
Đối chiếu [định nghĩa MAV_CMD](https://mavlink.io/en/messages/common.html#mav_commands).

## Đối chiếu với API/model hiện tại

Nguồn hiện có: `CreateMissionRequest`, `CreateWaypointRequest` và
`GET /api/missions/{id}/automation/plan`. Không tạo API hay sửa schema trong bản mẫu.

| Dữ liệu đích | DroneOps hiện có | Cần bổ sung hoặc quyết định |
| --- | --- | --- |
| Tọa độ | `latitude/longitude`; plan API có degrees và E7 | Đã đủ giá trị; adapter chọn cách encode đúng message/frame |
| Thứ tự | `sequenceOrder` | Tự sinh item order/seq và doJumpId; một waypoint có thể sinh nhiều command |
| Độ cao | `altitude`, `altitudeReference` | Altitude hiện nullable: phải đủ giá trị cho action cần nó; xác minh home/terrain trước khi map frame |
| Home | Chưa có planned home/home MSL trong mission DTO | Đọc HOME_POSITION từ FC, lưu snapshot phục vụ xác nhận; home offline cần lat/lon/alt MSL rõ ràng |
| Firmware/airframe | Có UID và system/component ID ở connection, chưa có profile firmware/airframe trong plan | Phát hiện ArduPilot/PX4, version, MAV_TYPE và capabilities từ FC; không mặc định mọi UAV là quadcopter |
| Tốc độ hành trình | `averageSpeedMetersPerSecond` | Chốt đây là target ground speed cho copter; biên dịch speed command được firmware hỗ trợ |
| Tốc độ tối đa | `maxSpeedMetersPerSecond` | Đây là giới hạn kế hoạch; không tự biến thành cruiseSpeed hay một parameter FC chung cho mọi firmware |
| Hành động | `actionType`: TakeOff/FlyThrough/Hover/CapturePhoto/Land | Adapter map action sang command; chưa có compiler thực thi |
| Hold time | Chỉ có tên action Hover | Thêm `holdTimeSeconds` hoặc dùng profile mặc định được công bố; mẫu chọn 5 giây |
| Heading | Chưa có yaw policy/yaw angle | Chọn hướng cố định, theo tuyến hoặc mặc định FC; không âm thầm gán 0° |
| Dung sai waypoint | Chưa có acceptance/pass radius | Có thể dùng cấu hình chuẩn theo profile thay vì bắt người dùng nhập từng điểm; kiểm tra firmware có hỗ trợ param từng item |
| Tiếp tục nhiệm vụ | Chưa có autoContinue riêng | Có thể sinh true cho tuyến cơ bản; phải định nghĩa hành vi pause nếu muốn hỗ trợ |
| Climb/descent | Chưa có tốc độ lên/xuống trong mission | Đọc/kiểm tra cấu hình FC; thêm override chỉ nếu sản phẩm cần và firmware cho phép |
| Geofence | Hệ thống có vùng hạn chế để validation | Chưa có exporter geofence onboard; không coi việc validate web là đã nạp fence xuống FC. Mẫu để trống |
| Rally/đáp dự phòng | Chưa có danh sách rally trong mission | Tùy chọn theo profile; mẫu để trống |
| Camera | Chỉ có CapturePhoto | Camera/component, kiểu trigger, interval/count, capability; chỉ cần khi dùng payload này |
| Failsafe/RTL | Chưa được chứng minh bởi plan API | Đọc/kiểm tra cấu hình pin, mất liên lạc/GPS, RTL altitude; thường là parameters onboard, không tự nhét vào `.plan` |

Không phải tất cả ô còn thiếu đều cần thêm field trên UI. Nhóm **người lập kế
hoạch chọn** gồm yaw/hold/dung sai khi cần override. Nhóm **đọc từ FC** gồm
firmware, airframe, home, capabilities và cấu hình failsafe. Nhóm **tự sinh** gồm
file version, seq, command, frame và autoContinue theo adapter/profile đã chọn.

## Những dữ liệu giữ ở backend/gateway

Các trường hiện có `missionId`, `uavId`, `planHash`, `scheduledStartUtc`,
`scheduledEndUtc`, lựa chọn tự động và xác nhận người dùng không phải trường bay
tiêu chuẩn trong `.plan`. Backend dùng chúng để cho phép hoặc chặn việc thực thi.
Ví dụ lịch 05/11/2026 09:00 GMT+7 được giữ dưới dạng `2026-11-05T02:00:00Z`.
File mission không tự hiểu lịch đó, xác nhận T−5 hoặc điều kiện dừng tại EndTime.

Luồng điều khiển còn cần commandId, deadline, UID/connection, source planHash,
compiled mission hash, ACK upload, read-back và ACK lệnh. Upload thành công không
đồng nghĩa đã ARM hay bắt đầu bay; EndTime cũng không tự khiến FC dừng/hạ cánh.
Phải chốt hành vi hết giờ/mất mạng bằng scheduler và failsafe onboard.

## ArduPilot và PX4

Mẫu chọn ArduPilot. PX4 có firmwareType 12, nhưng đổi riêng số 3 thành 12 chưa
chứng minh mission tương thích: adapter vẫn phải kiểm tra firmware version,
airframe, command support, altitude, home/seq và behavior ARM/start.
Chỉ multicopter được mô tả ở đây; fixed-wing/VTOL cần profile takeoff, loiter,
approach/landing riêng. Đặc biệt hold tại waypoint không có cùng ý nghĩa với
fixed-wing. Không dùng firmwareType hoặc vehicleType khai trong file thay cho
phát hiện và đối chiếu thiết bị thực tế.
