# Kiểm tra Pixhawk qua Jetson — 05/11/2026

## Kết quả đã thực hiện

Đọc trực tiếp MAVLink từ FC qua MAVLink Router trên Jetson. Không gửi ARM,
DISARM, MISSION_START, thay đổi mode, PARAM_SET hoặc upload nhiệm vụ trong lần kiểm tra.
Không đưa UID, token, mã pairing hoặc tọa độ chính xác vào tài liệu.

- Chủ thiết bị xác nhận đang nối/cấp FC qua USB C–C; chưa xác nhận pin bay qua power module, số cell và kết nối GPS.
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

## Kiểm tra tiếp ngày 06/11/2026

- Chủ thiết bị xác nhận nguồn bay là **LiPo 4S2P**; đã từng cung cấp con số
  **10.000 mAh** nhưng nay cho biết nhãn pin không ghi dung lượng và chưa chắc
  chắn về con số đó. Dung lượng thực **chưa xác minh**. FC và Jetson cùng được
  cấp nguồn, toàn bộ cánh quạt đã tháo và drone cố định trên bàn.
- Đọc trực tiếp cổng USB thứ hai của FC, chỉ nhận MAVLink: power module báo
  khoảng **16,0 V**, **0,41 A**, SOC **98%**, consumed charge tăng theo thời gian.
  Một cửa sổ khoảng 20 phút tăng từ 32 lên 171 mAh, phù hợp xấp xỉ với dòng
  0,412 A khi đứng yên. Đây là kiểm tra phép đo **trên bàn**, chưa phải hiệu
  chuẩn dung lượng hoặc mức tiêu thụ khi bay.
- `BATT_CAPACITY` trong FC là **8.000 mAh**: đây là tham số cấu hình đã đọc từ
  FC, không phải số đo và không chứng minh pin có dung lượng 8.000 mAh. Chưa có
  nguồn xác thực dung lượng thực của pack. Không suy dung lượng thật từ SOC
  hoặc số mAh đã tiêu thụ; chưa thay tham số dung lượng hay ngưỡng pin.
- Chủ thiết bị đã đối chiếu trong **Mission Planner** và xác nhận FC hiển thị
  **8.000 mAh**. Như vậy giá trị cấu hình/nguồn đọc đã được xác nhận bằng GCS
  thứ hai; app ưu tiên tự lấy giá trị này, không yêu cầu nhập lại. Điều này
  không phải phép đo dung lượng sử dụng thực của pack.
- Audit xuất `batteryCapacityEvidence`: số cấu hình từ `BATT_CAPACITY`, dung
  lượng đo thực để trống, `capacityVerified=false` và
  `remainingCapacityVerified=false`. Dung lượng cấu hình dương không chứng minh
  đủ năng lượng bay. Cần dữ liệu nhà sản xuất hoặc phép đo dung lượng sử dụng
  được bằng thiết bị phù hợp cùng hiệu chuẩn power module; không xả cạn pin
  bằng động cơ trên bàn để tìm dung lượng.
- Adapter kiểm tra thứ tự low/critical, ngưỡng mAh không vượt số cấu hình và
  đúng hành động low RTL/critical LAND. Các kiểm tra này chỉ kiểm tra tính nhất
  quán, không chứng nhận ngưỡng phù hợp pin thực. 10 bài unit test adapter/audit
  đạt; chạy lại adapter cập nhật trên SITL 4.7.1 đọc lại đủ 13 tham số thành công.
- RadioMaster Pocket ELRS đã bind với receiver theo xác nhận của chủ thiết bị.
  FC nhận `RC_CHANNELS` trên các kênh 1–8. Khi chủ thiết bị di chuyển cần phải,
  kênh 1 và 2 thay đổi; kênh 3 cũng thay đổi và cuối phiên đọc ở khoảng 1386 µs.
  Cần xác nhận lại ánh xạ/hiệu chuẩn và ga thấp trước mọi phép thử ARM. Kênh 7
  đang giữ ở khoảng 1503 µs; công tắc ARM riêng chưa được cấu hình hay thử.
- GPS vẫn ở trong nhà, fix type 1, chưa có Home. Chủ thiết bị sẽ kiểm tra lại
  ngoài trời. Không thay bằng GPS/Home giả để mở khóa bay thật.
- Chỉ thực hiện quan sát thụ động; không gửi ARM, DISARM, thay mode, PARAM_SET
  hoặc upload mission vào FC thật.
- Mã nguồn ArduCopter tag `Copter-4.7.1` đã biên dịch SITL thành công trên
  Jetson. Harness riêng kết nối `127.0.0.1` tới simulator, nhận diện firmware
  4.7.1, áp dụng và đọc lại độc lập 13 tham số envelope/failsafe. Kết quả
  `parameterCount=13`, `independentReadback=true`. Đây mới là kiểm tra tham số
  trên mô phỏng; chưa chứng minh hành vi bay hoặc các lỗi kết hợp và không tạo
  quyền bay thật.
- Bài autotest chính thức `test.Copter.BatteryFailsafe` của ArduPilot 4.7.1
  đã **PASS** trên SITL. Nó thử nhiều nhánh pin thấp/nguy cấp và phản ứng RTL/
  LAND của firmware mô phỏng. Bài này dùng cấu hình pin mô phỏng của ArduPilot,
  không xác nhận ngưỡng điện áp hay dung lượng của pack 4S2P thực tế.
- Các bài autotest chính thức `test.Copter.GCSFailsafe`,
  `test.Copter.ThrottleFailsafe` (mất RC) và `test.Copter.MaxAltFence` đều
  **PASS** trên SITL. Chưa có bài tích hợp xác nhận đồng thời lỗi pin + RC/GCS
  + mất GPS/vi phạm giới hạn với đúng envelope đã duyệt của ứng dụng.

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

- Kiểm tra lại trên FC và mã nguồn tag 4.7.1 xác nhận `ARMING_SKIPCHK=0`
  thay cho tham số legacy `ARMING_CHECK`: không có kiểm tra arming nào bị bỏ.
  Audit/gateway đã sửa để không chặn sai vì thiếu tên cũ.
- Sau reboot, simulator được dựng lại trong `/home/dronelab/droneops-sitl`.
  Harness adapter mới đã PASS trên ArduCopter 4.7.1, đọc lại 13 tham số với
  `nonblockingAdapter=true`. Không tính đây là kiểm thử toàn luồng bay/lỗi kết hợp.
- Lần đọc FC mới nhất: disarmed/on-ground, pin khoảng 15,50 V/0,44 A; GPS dao
  động từ 3D fix/4 vệ tinh tới không fix/0 vệ tinh, chưa có Home. Tín hiệu tại
  vị trí đang bị che chưa đủ điều kiện bay tự động. Chủ thiết bị xác nhận cánh
  đã tháo, nhưng không thể chuyển GPS ra vị trí thoáng hơn trong phiên này.

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
