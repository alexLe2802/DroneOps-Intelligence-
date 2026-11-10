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


## Không yêu cầu chọn Planned Home

Màn hình tạo/sửa nhiệm vụ không có bước chọn Home. Click đầu tiên tạo TakeOff,
sau đó thêm các điểm bay và Land; số thứ tự waypoint bắt đầu từ 1.
Backend không yêu cầu `Navigation.PlannedHome`; gateway không dùng metadata
Planned Home cũ làm điều kiện cấu hình, upload, ARM hoặc START.
Home thực tế vẫn do flight controller quản lý, không bị đặt lại từ bản đồ.
Profile ArduCopter hiện tại vẫn kiểm tra TakeOff gần Home thực tế, GPS/navigation,
pre-arm, UID, hash/read-back, consent và thời hạn lệnh như trước.

## Điểm kết thúc: Land hoặc Return/RTL

Tên hiển thị `Takeoff/Home` vẫn lưu action `TakeOff`; không thêm lại bước chọn Planned Home.
Điểm cuối cho chọn `Land` hoặc `Return/RTL` (action API `Return`).

- A Takeoff/Home → B Land: đến B ở độ cao kế hoạch, sau đó LAND tại B.
- A Takeoff/Home → B Return/RTL: đến B ở độ cao kế hoạch, sau đó NAV_RETURN_TO_LAUNCH (20)
  quay về Home thực tế trên FC và hạ cánh. B không bị bỏ qua; RTL không chứa tọa độ Home do web tự đặt.
- Bản đồ vẽ thêm chặng B → A khi Return được chọn. Validation vùng bay, thời gian và ước tính pin
  bao gồm chặng về, dùng độ cao trần envelope để ước tính bảo thủ; Home FC thực tế phải qua kiểm tra TakeOff hiện có.
- Gateway đọc lại cấu hình FC trước upload/ARM/START: RTL_ALT_FINAL_M=0 (hạ cánh),
  RALLY_TOTAL=0, RTL_ALT_TYPE=0 (Home-relative), RTL_ALT_M trong trần đã duyệt.
  Thiếu/cũ/không phù hợp sẽ chặn, không tự ghi đè tham số để ép bay.
- Return chỉ Completed khi có bằng chứng đến B, thực thi terminal, hai quan sát landed/disarmed
  và telemetry vị trí mới gần Home thực tế (10 m). Đến B hoặc ACK RTL không tự hoàn thành nhiệm vụ.

Tham khảo: https://ardupilot.org/copter/docs/common-mavlink-mission-command-messages-mav_cmd.html
Không chạy test local hoặc bay thật trong lượt triển khai; cập nhật fixture để CI/người vận hành kiểm tra sau.

## Thời điểm gửi và thực thi nhiệm vụ

Manager Approved không tự ARM drone. Sau approval, khi gateway đúng UID kết nối,
FC disarmed/landed và profile tham số đã xác minh, backend tự queue Upload trước giờ bay.
Gateway truyền mission rồi tải lại để so khớp compiled hash. ACK upload không tạo chuyến bay.
Pilot xác nhận đúng kế hoạch/kết nối trong [T−5 phút,T), giữ portal visible/online.
Tại T, scheduler kiểm tra lại approval, pending update, eligibility/schedule, preflight Pass
trong 60 giây, telemetry/gateway/pilot presence, UID, mission hash/read-back, envelope và pre-arm.
Đủ điều kiện mới ARM → START trong cửa sổ T đến T+5 giây. Không tự bay bù nếu bị chặn/hết hạn.
FC thực hiện Takeoff/Home → các waypoint → Land hoặc Return/RTL; chỉ quan sát in-air
mới tạo execution. DISARM không phải lệnh cất cánh. Kết thúc cần landed/disarmed evidence.
