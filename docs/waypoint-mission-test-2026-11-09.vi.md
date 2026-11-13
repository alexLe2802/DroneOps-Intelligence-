# Kiểm tra nhận và thực thi mission — 09/11/2026

## Kết luận

Chưa xác nhận nền tảng hiện tại điều khiển drone thật bay hoàn thành mission.
ARM và quay motor không cánh do người dùng thực hiện không chứng minh upload,
đọc ngược hoặc thực thi waypoint từ ứng dụng.

## Kết quả quan sát

- Gateway unit: 87/87 PASS, gồm test MAVLink UDP localhost và serial PTY.
- Mission compiler: 29 kiểm tra PASS; flight-control state: 20 kiểm tra PASS.
- SSH Jetson hoạt động; droneops-gateway và droneops-router đều active.
- Service gateway đang chạy không có cờ `--enable-flight-control`. Bộ file đang
  cài trong `/opt/droneops-gateway` thiếu adapter và envelope verifier của bản mới.
  Chưa nâng cấp hoặc bật thực thi trên Jetson trong lần kiểm tra này.
- Log gateway 10 phút gần nhất có GPS fix_type=3, thường 8–10 vệ tinh; có một
  mẫu fix_type=2/3 vệ tinh. Không dùng số vệ tinh để thay cho toàn bộ readiness.
- Không đọc trực tiếp được FC qua cổng USB phụ: tài khoản SSH không thuộc nhóm
  dialout; sudo không mật khẩu không được cấp. Không đổi quyền, không dừng router
  hoặc gateway, không mở cổng serial chính đang do router sử dụng.

## Thử tích hợp ArduCopter thật trong SITL

Harness mới: `gateway/sitl_waypoint_mission.py`. Nó tự khởi chạy binary
ArduCopter 4.7.1 đã biên dịch trên Jetson, dùng thư mục tạm, instance 11 và cổng
TCP localhost 5870; không có đối số endpoint cho phép trỏ sang FC thật.
Không sử dụng session production. Tiến trình simulator được dừng khi kết thúc.

Harness dùng chính FlightControl/EnvelopeVerifier/adapter hiện tại để thử:
áp dụng giới hạn cơ bản → upload → read-back → ARM → 10 giây → START → waypoint
→ LAND. Chỉ các bước trước ARM đã chạy được:

1. Firmware được nhận diện 4.7.1; giới hạn cơ bản đã qua read-back.
2. FC mô phỏng yêu cầu đủ 6 item (bao gồm header Home).
3. FC mô phỏng trả MISSION_ACK type=0 và STATUSTEXT `Flight plan received`.
4. MISSION_COUNT đọc ngược bằng 6.
5. Header Home đọc ngược khớp. Item TAKEOFF seq=1 đọc ngược có cùng frame,
   tọa độ và độ cao 15 m, nhưng param4=0 thay vì NaN mà gateway gửi.
6. Hàm same_item hiện tại từ chối khác biệt này, nên Upload báo failed.
   Không có ARM/START và không có kết quả bay/đáp trong lần thử này.

Mã AP_Mission.cpp tại tag Copter-4.7.1 xác nhận TAKEOFF không lưu trường yaw.
Firmware còn không lưu acceptance radius/yaw trong NAV_WAYPOINT của Copter;
đối chiếu generic từng trường hiện tại cần được rà soát theo firmware. Không nên
nới lỏng toàn bộ read-back hoặc nhận một yaw/radius đã bị firmware bỏ qua là
thông số được thực thi. Cần xử lý khả năng hỗ trợ trong compiler và quy tắc
canonical hóa của adapter, bổ sung regression test rồi chạy lại harness.

Lệnh chạy trên Jetson (sau khi đặt các module gateway cùng thư mục harness):

```sh
/home/dronelab/droneops-sitl/venv/bin/python sitl_waypoint_mission.py \
  --binary /home/dronelab/droneops-sitl/ardupilot/build/sitl/bin/arducopter \
  --defaults /home/dronelab/droneops-sitl/ardupilot/Tools/autotest/default_params/copter.parm
```

Không upload mission, ghi tham số, ARM/DISARM hoặc đổi mode trên FC thật.
Không triển khai production. Việc bay theo waypoint trên phần cứng thật vẫn
chưa được xác minh.

Tham chiếu: [AP_Mission.cpp, Copter-4.7.1](https://github.com/ArduPilot/ardupilot/blob/Copter-4.7.1/libraries/AP_Mission/AP_Mission.cpp).

## Cập nhật sau khi người dùng yêu cầu thử ARM và mission

- Đã dùng quyền sudo do người dùng cấp để đọc FC thật và gửi ARM thông thường.
  FC ban đầu từ chối vì Disarm Switch và ga cao; sau khi người dùng chỉnh tay
  cầm, ACK=0 và heartbeat xác nhận ARMED. Lần đọc sau xác nhận DISARMED, không
  cần gửi thêm DISARM. Đây là lệnh trực tiếp, không phải mission từ nền tảng.
- Đã sửa canonical hóa Copter trước upload: NAV yaw dùng placeholder theo
  firmware, acceptance radius yêu cầu khớp tham số FC hiện tại, hold cần số giây
  nguyên. Không bỏ qua khác biệt tọa độ, độ cao, frame hoặc nhận yaw override
  không được firmware hỗ trợ là đã thực thi. Guard bán kính FC được thêm để
  thay đổi tham số sau xác minh làm mất receipt.
- Bộ unit gateway sau sửa: 90/90 PASS.
- SITL 4.7.1 sau sửa: Upload/read-back, ARM, START, airborne, waypoint và
  LAND/DISARM đều PASS. Các item được xác nhận reached: 2, 3, 4; độ cao tương
  đối lớn nhất 15.036 m. Simulator chạy 5x; DISARM_DELAY=60 chỉ trong cấu hình
  mô phỏng để đủ thời gian cho countdown 10 giây theo đồng hồ thực. FC thật
  hiện DISARM_DELAY=10, chưa thay đổi. Không suy ra countdown thực tế sẽ vượt
  được cửa sổ auto-disarm đó.
- FC thật báo GPS fix 3D/11 vệ tinh, ON_GROUND, DISARMED, mode LAND tại lần
  snapshot chuẩn bị mission. ARMING_SKIPCHK=2048, không được công cụ sửa lại.
  Vì vậy cấu hình hiện tại vẫn không đạt gate production yêu cầu skip-check=0.
- Người dùng yêu cầu chuẩn bị mission thử riêng tại vị trí drone, sở hữu bởi
  pilot@droneops.com và duyệt bởi manager@droneops.com. Đang dùng chính dịch vụ
  validation/workflow của ứng dụng để tạo và xét duyệt; chưa khẳng định đã có
  mission Approved hoặc đã upload/START trên FC thật.

### Kết quả chuẩn bị mission đứng tại chỗ

- Tuyến dự kiến: cùng tọa độ GPS FC 10.8417588, 106.8091690 cho TakeOff,
  Hover 3 giây và Land; độ cao Relative 11.5 m, cruise/max 0.5 m/s, trần 15 m.
  Ground elevation không được suy đoán; planned Home lấy từ FC riêng biệt.
- Hai tài khoản được kiểm tra là chủ drone/Pilot và operation_manager đúng
  theo chỉ định của người dùng. Không tạo token giả hoặc sửa trực tiếp Status.
- Công cụ quản trị gọi chính MissionWorkflowService.SavePlanAsync và dự định
  gọi MissionApprovalService.ApproveMissionAsync; không chạy migrations/DDL.
- Ngày 09/11/2026, validation stage Creation trả Fail vì cả ba waypoint và
  hai đoạn đều giao vùng `MOD-CB-SUPPLEMENT / PART 777`, revision 3,
  geofence 35c15aa2-0c34-4fd6-959b-85efcf726d2a. Workflow dừng trước khi tạo
  mission hoặc ghi approval. Không tắt geofence, không thay tọa độ để mở khóa.
- Payload dự kiến giữ ở `/private/tmp/droneops-stationary-bench-plan.json`
  trong workspace máy phát triển; nó chưa phải mission được duyệt trên nền tảng.
- FC thật chưa được upload mission hoặc gửi START trong lần yêu cầu này.
  Kết quả thực thi toàn bộ waypoint hiện chỉ được chứng minh trên SITL.
- Kết nối database trực tiếp từ mạng máy phát triển bị thay chứng chỉ bởi
  firewall. Đã dùng SSH tunnel tạm qua Jetson, vẫn xác minh CA Supabase và
  hostname gốc. Không bỏ kiểm tra TLS, không đổi cấu hình production.
