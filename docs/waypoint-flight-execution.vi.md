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
5. Phi công xác nhận trong [T−5 phút, giờ bắt đầu), giữ Live Monitoring online/visible
   và đặt FC ở LOITER. Không có nút ARM thủ công cho mission Automatic.
6. Đến giờ bay, scheduler lưu dispatch identity và kiểm tra lại toàn bộ gate, phát ARM
   rồi START đúng một lần trong deadline start + 5 giây. Không đếm 10 giây hoặc bay bù.
7. ACK chỉ là xác nhận lệnh. Telemetry armed/in-air/mission mode mới tạo execution và
   InProgress; MISSION_ITEM_REACHED theo mapping compiler mới tăng progress.
8. Đạt tuyến và FC xác nhận landed/disarmed qua hai quan sát riêng thì Completed,
   lưu terminal evidence và queue report. Hạ cánh sớm giữ Aborted/partial evidence.
   Quá giờ lịch, mất mạng hoặc disarm đơn lẻ không tạo completion thành công.

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
Cần migration execution/realtime mới; chưa triển khai production hoặc chạy lại test trong lượt 10/11/2026.

Xem [phạm vi một drone và bài nghiệm thu KAN-51](realtime-mainflow-completion.vi.md).

Tham chiếu lệnh chính thức: https://ardupilot.org/copter/docs/common-mavlink-mission-command-messages-mav_cmd.html


## Planned Home bắt buộc cho nhiệm vụ tự động

Planned Home là vị trí cất cánh dự kiến do người lập kế hoạch chọn, được lưu ở
`Navigation.PlannedHome` của waypoint 1. Nhiệm vụ tự động phải có trường này khi
lưu và khi kiểm tra/biên dịch; nhiệm vụ cũ thiếu Home phải bổ sung trước khi bay.
Không tự suy ra Home từ TakeOff để bỏ qua yêu cầu chọn Home.

Theo [QGroundControl Plan View](https://docs.qgroundcontrol.com/Stable_V5.1/en/qgc-user-guide/plan_view/plan_view.html),
Planned Home phục vụ lập kế hoạch; Home thực tế do firmware xác lập khi arm.
Với [ArduCopter RTL](https://ardupilot.org/copter/docs/rtl-mode.html), Home thường
là vị trí drone khi arm với GPS hợp lệ. Điểm chọn trên bản đồ không phải lệnh bay
đến Home và không được ghi đè Home thực tế của FC.

Gateway kiểm tra trước upload, arm và start: Home FC mới trong 15 giây và vị trí
ước lượng hiện tại `GLOBAL_POSITION_INT` mới trong 3 giây đều phải cách Planned
Home không quá 10 m theo khoảng cách ngang địa lý. Nếu khai báo cao độ Home MSL,
cao độ Home FC phải lệch không quá 5 m; bỏ trống cao độ thì dùng Home thực tế.
Các điều kiện GPS/navigation, landed, pre-arm và kiểm tra TakeOff vẫn áp dụng.
Ngưỡng 10 m là chính sách DroneOps hiện tại, không phải bảo đảm mặc định của QGC
hay bán kính waypoint. Không tự nới ngưỡng khi GPS kém hoặc tự di chuyển drone.

- Drone tại FPT TP.HCM, Planned Home tại Đồng Nai: chặn upload/arm/start qua
  DroneOps. Người vận hành phải đưa drone đến khu vực cất cánh hoặc sửa kế hoạch
  và xác nhận lại. Arm bằng RC/QGC bên ngoài không được DroneOps ngăn chặn; arm
  thành công không đồng nghĩa nhiệm vụ đã đủ điều kiện bắt đầu.
- Drone lệch Home trên map vài mét: cho phép nếu cả Home FC và vị trí hiện tại
  nằm trong 10 m và các kiểm tra khác đạt. Không so sánh tọa độ bằng tuyệt đối.
- Home FC đúng nhưng drone đã được mang sang nơi khác: vị trí hiện tại không
  khớp nên chặn. Thiếu hoặc hết hạn dữ liệu vị trí cũng chặn.

Cần SITL và kiểm tra thực địa trước khi dùng trên phần cứng; các kiểm thử logic
không chứng minh được độ chính xác GPS hoặc vật cản tại nơi cất cánh.


### Thứ tự điểm khi lập kế hoạch automatic

Trên màn hình tạo/sửa và chi tiết nhiệm vụ, điểm 1 là Home, điểm 2 là TakeOff,
điểm cuối là Land. Khi chưa chọn Home, click bản đồ đầu tiên đặt Home; click tiếp
đặt TakeOff; click thứ ba đặt Land. Khi thêm điểm nữa, Land cũ chuyển thành
FlyThrough và điểm mới trở thành Land, giữ cơ chế thêm waypoint hiện tại.
Có thể đặt/sửa Home bằng ô tọa độ, nút chọn trên map hoặc kéo marker Home.
Clear Route trong chế độ automatic xóa cả Home và các điểm bay để bắt đầu lại.

Home là điểm lập kế hoạch, lưu trong `Navigation.PlannedHome`, không phải một
lệnh waypoint gửi tới FC. Số thứ tự hiển thị của điểm bay được cộng 1 trong chế
độ automatic; `SequenceOrder` trong API vẫn bắt đầu từ 1 cho TakeOff để giữ
nguyên biên dịch, kiểm tra và thực thi nhiệm vụ. Manual giữ thứ tự hiện tại.
