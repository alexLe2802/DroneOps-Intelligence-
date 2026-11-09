# Thực thi mission theo waypoint

## Swimlane luồng bay tự động

Sơ đồ tiếp nối mission Automatic đã được duyệt ở MF1, dùng cùng phong cách
swimlane của MF1. Các lane thực thi gồm Pilot, Mission Management Service,
Trusted Gateway và Flight Controller/UAV.

[Mở sơ đồ draw.io để chỉnh sửa](diagrams/Swimlane%20Diagram/automatic-waypoint-flight.drawio)
· [Xem bản SVG](diagrams/Swimlane%20Diagram/automatic-waypoint-flight.svg).

![Swimlane thực thi bay tự động](diagrams/Swimlane%20Diagram/automatic-waypoint-flight.svg)

Luồng giữ nguyên mission hiện tại: chọn Automatic, chọn các waypoint theo thứ tự,
đặt độ cao/tốc độ và thời gian, gửi duyệt. Điểm đầu là TakeOff, điểm cuối là Land;
các điểm giữa dùng FlyThrough hoặc Hover (cần thời gian dừng).
Không giới hạn tuyến ở hai điểm và không thêm điều khiển camera.

## Chuẩn bị và chạy

1. Mission Automatic đã Approved, có UAV và khoảng thời gian còn hiệu lực.
2. Kết nối gateway với đúng drone/mission. Backend đặt `FlightExecution__Enabled=true`;
   gateway khởi chạy với `--enable-flight-control`. Hai công tắc này vẫn phải bật
   có chủ đích; sửa mã nguồn không bật lệnh trên drone đang kết nối.
3. Với ArduCopter 4.7.1, gateway đọc GPS, Home, phiên bản và tham số mới từ FC.
   Điểm TakeOff phải cách Home thực tế không quá 10 m. Độ cao mission dùng Relative.
   Không thay Home thực tế bằng tọa độ click bản đồ.
4. Backend tự giải quyết tùy chọn từ FC, gateway áp dụng và đọc lại giới hạn tốc độ,
   rồi tải mission và tải ngược để đối chiếu từng item. Nếu không tự chuẩn bị được,
   xem Flight readiness và FC MISSION SETTINGS trong Live Monitoring.
5. Phi công xác nhận mission hiện tại khi preflight đạt yêu cầu. Đến thời gian bắt đầu,
   đặt FC ở LOITER (ArduCopter), giữ Live Monitoring online và nhấn ARM.
6. Sau ACK và heartbeat xác nhận ARM, hệ thống đếm 10 giây rồi gửi MISSION_START
   đúng một lần. Drone cất cánh, đi lần lượt qua các waypoint, đến điểm cuối ở độ cao
   đã đặt rồi hạ cánh. Hover giữ đúng thời gian đặt. Có thể hủy đếm ngược và DISARM
   khi FC xác nhận còn trên mặt đất.

ArduCopter LAND dùng bộ phát hiện hạ cánh của FC và bỏ qua trường độ cao của lệnh
LAND; vì vậy không bắt nhập ground elevation để chạy tuyến cơ bản. Trường độ cao
waypoint cuối vẫn dùng cho chặng tiếp cận trước LAND. PX4 giữ yêu cầu ground elevation
và cần adapter runtime riêng; không coi nhánh ArduCopter là xác minh PX4.

## Recovery được hoãn

Mặc định `MissionCompileOptions.configureRecovery=false`. Gateway chỉ áp dụng
`WP_SPD`, `WP_SPD_UP`, `WP_SPD_DN`, `LAND_SPD_MS`; không ghi FS_*, BATT_*, FENCE_* hay
RTL_*. Failsafe đang cấu hình trên FC tiếp tục giữ nguyên. Không bắt khai báo mô hình
năng lượng recovery hoặc survey bãi đáp để dùng tuyến cơ bản.

Adapter recovery đầy đủ và các kiểm thử cũ vẫn giữ lại. Có thể bật cho kế hoạch
trong FC MISSION SETTINGS, hoặc đặt backend `FlightExecution__ConfigureRecovery=true`
để dùng khi tự giải quyết tùy chọn mới. Chọn chế độ này yêu cầu các tham số pin và
recovery đầy đủ như trước. Lựa chọn được gắn vào compiled hash; thay đổi cần upload
và xác nhận lại. Không tự tắt arming checks, GPS/Home hoặc kiểm tra upload để bỏ qua
recovery.

## Phạm vi xác minh

Kiểm thử phần mềm mô phỏng trao đổi MAVLink bao phủ áp dụng giới hạn không có
recovery, upload/read-back, ARM, đếm ngược và START. Đây không phải kết quả bay thật.
Cần kiểm tra thực địa GPS/Home, nguồn pin, RC/LOITER và bay thử với phần cứng thực tế.
Không có thay đổi dữ liệu/schema hoặc triển khai production trong thay đổi này.

Tham chiếu lệnh chính thức: https://ardupilot.org/copter/docs/common-mavlink-mission-command-messages-mav_cmd.html
