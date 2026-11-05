# Kiểm tra Pixhawk qua Jetson — 05/11/2026

## Kết quả đã thực hiện

Đọc trực tiếp MAVLink từ FC qua MAVLink Router trên Jetson. Không gửi ARM,
DISARM, MISSION_START, thay đổi mode, PARAM_SET hoặc upload nhiệm vụ trong lần kiểm tra.
Không đưa UID, token, mã pairing hoặc tọa độ chính xác vào tài liệu.

- Board USB: Holybro Pixhawk 6C; cổng dữ liệu `if00`.
- Firmware: ArduPilot / ArduCopter **4.7.1**, release type 255;
  custom firmware revision đọc từ FC: `dbe79216`.
- MAVLink system/component: **1/1**; vehicle type 2 (quadrotor).
- Trạng thái: **DISARMED**, **STABILIZE**, `landed_state=1`.
- GPS: `fix_type=1`, **0 vệ tinh**; không nhận được Home hợp lệ.
- Battery monitor: `BATT_MONITOR=4`; `BATT_CAPACITY=8000` mAh.
  8000 là khai báo trong FC, không phải phép đo dung lượng thực.
- Điện áp pack đọc được **0,003 V**, dòng khoảng **0,46 A** nhưng SOC báo **99%**.
  Không dùng SOC này để xác nhận đủ năng lượng bay. Cần kiểm tra nguồn pin,
  power module và hiệu chỉnh đo trước khi tính năng lượng.

## Thông số FC hiện có, chưa thay đổi

| Mục | Giá trị đọc từ FC | Ý nghĩa với luồng app |
|---|---|---|
| `WP_SPD` | 10 m/s | Cao hơn giới hạn profile thử nghiệm 5 m/s của app |
| `WP_SPD_UP` | 2,5 m/s | Cao hơn trần lên 1,5 m/s của app |
| `WP_SPD_DN` | 1,5 m/s | Cao hơn trần xuống 1 m/s của app |
| `WP_RADIUS_M` | 2 m | Giá trị bán kính waypoint có thể dùng cho bản biên dịch xem trước |
| `WP_ACC` | 2,5 m/s² | Giá trị firmware; chưa phải policy gia tốc được duyệt |
| `LAND_SPD_MS` | 0,5 m/s | Đơn vị m/s; khác profile app hiện có, chưa tự đổi |
| `LAND_SPD_HIGH_MS` | 0 | Firmware sử dụng tốc độ xuống WP cho giai đoạn đầu |
| `RTL_ALT_M` | 15 m | Chưa xác minh khoảng hở địa hình/vật cản |
| `RTL_SPEED_MS` | 0 | Firmware dùng tốc độ WP; hiện WP là 10 m/s |
| `RTL_ALT_FINAL_M` | 0 m | Cấu hình kết thúc RTL bằng hạ cánh |
| `FENCE_ENABLE` | 0 | Geofence đang tắt |
| `FENCE_ALT_MAX` | 120 m | Không có tác dụng chặn độ cao khi fence chưa bật |
| `FS_GCS_ENABLE` | 0 | Phản ứng khi mất GCS đang tắt |
| `FS_GCS_TIMEOUT` | 5 s | Timeout cấu hình, không có nghĩa failsafe đang bật |
| `FS_OPTIONS` | 16 | Cần xét cùng mode và chính sách mất GCS; không coi là đã xác minh |
| `FS_THR_ENABLE` | 1 | Cấu hình RC-loss RTL, chưa thử bằng lỗi RC thực tế |
| `FS_EKF_ACTION` | 1 | Cấu hình xử lý lỗi EKF, chưa thử thực tế |
| `BATT_LOW_VOLT` / `BATT_CRT_VOLT` | khoảng 12,4 / 12 V | Chưa xác nhận phù hợp loại pin/số cell thực tế |
| `BATT_LOW_MAH` / `BATT_CRT_MAH` | 0 / 0 | Ngưỡng theo dung lượng còn lại đang không bật |
| `BATT_FS_LOW_ACT` / `BATT_FS_CRT_ACT` | 2 / 1 | Cấu hình low RTL, critical Land; phụ thuộc đo pin hoạt động đúng |

## Những phần phần mềm đã bổ sung từ kiểm tra này

- Gateway đọc đúng các tên mới của ArduCopter 4.7, gồm `WP_RADIUS_M`,
  `LAND_SPD_MS`, `LAND_SPD_HIGH_MS`, `RTL_ALT_M`, `RTL_SPEED_MS`.
- Compiler đọc `WP_RADIUS_M` theo mét, `WP_SPD` theo m/s. Vẫn hỗ trợ
  `WPNAV_RADIUS` và `WPNAV_SPEED` theo centimet cho firmware cũ.
- AI giữ lại SOC gốc để quan sát nhưng không kết luận đáp ứng nhiệm vụ khi
  điện áp pack báo dưới 1 V. Không thay dữ liệu thiếu bằng thông số pin giả.
- Thêm `gateway/fc_audit.py`: audit chỉ đọc, có timeout, lọc nguồn FC,
  báo tham số không nhận được, loại NaN/Infinity và không xuất UID/tọa độ chính xác.
  Công cụ không gửi heartbeat GCS nên không tự thiết lập một GCS failsafe lease.
- Đã sao lưu mã gateway cũ dưới `/var/backups/droneops-gateway/` trên Jetson,
  cập nhật module mới, ghép bằng mã một lần do chủ UAV cung cấp.
  Service chạy và enabled cho các lần bật máy sau; chưa thử reboot vật lý.
- Tái sử dụng API và cấu trúc dữ liệu hiện có; không tạo endpoint trùng chức năng,
  không đổi schema DB và không chạy migration trong lần này.

## Điều kiện vẫn chặn ARM/Start tự động

Việc đọc firmware đã hoàn tất cho FC này, nhưng chưa chứng minh cấu hình bay an toàn.

1. Nguồn pin thực, điện áp/dòng đo hợp lý, loại pin/số cell và dung lượng lắp thật.
2. GPS/EKF đủ điều kiện, Home hợp lệ và dữ liệu sức khỏe FC mới.
3. Kế hoạch Automatic đã duyệt, điểm LAND/điểm dự phòng được khảo sát và mô hình
   năng lượng hiệu chỉnh cho đúng pin, tải và cấu hình drone.
4. Adapter áp dụng và đọc lại toàn bộ limits/failsafe đúng bản firmware,
   nhận diện thay đổi từ GCS khác, ràng buộc receipt với compiled mission hash.
   Chỉ đọc các giá trị hiện tại không được tạo `verifiedEnvelopeHash`.
5. Kiểm thử SITL lỗi mất RC/GCS, pin thấp/critical, fence/overspeed và lỗi kết hợp;
   sau đó kiểm thử bàn/ngoài trời có giám sát. Chưa thực hiện trong lần audit này.
6. PX4 vẫn cần xác minh riêng trên SITL/FC PX4; FC ArduCopter này không thể
   xác nhận hành vi PX4.

Các cờ thực thi vẫn tắt. Không tự tắt arming checks, tạo GPS/Home giả hoặc sửa
các ngưỡng điện áp khi chưa biết pin thực để vượt điều kiện chặn.

## Nguồn đối chiếu theo phiên bản

- [AC_WPNav, tag Copter-4.7.1](https://github.com/ArduPilot/ardupilot/blob/Copter-4.7.1/libraries/AC_WPNav/AC_WPNav.cpp)
- [Landing parameters, tag Copter-4.7.1](https://github.com/ArduPilot/ardupilot/blob/Copter-4.7.1/ArduCopter/mode_land.cpp)
- [RTL parameters, tag Copter-4.7.1](https://github.com/ArduPilot/ardupilot/blob/Copter-4.7.1/ArduCopter/mode_rtl.cpp)
- [GCS failsafe](https://ardupilot.org/copter/docs/gcs-failsafe.html)
- [Battery failsafe và yêu cầu power module](https://ardupilot.org/copter/docs/failsafe-battery.html)
