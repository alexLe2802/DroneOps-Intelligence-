# Automatic mission execution: data contract and failsafe proposal

Trạng thái: **đề xuất chờ duyệt**. Phạm vi ban đầu: multicopter ArduPilot/PX4.
Camera hoãn. Fixed-wing/VTOL cần profile riêng.

Đã triển khai bước đầu: MissionCommandCompiler và compilationPreviews trong
GET /api/missions/{id}/automation/plan. Đây là preview, không có đường phát lệnh
upload/ARM/start. CanEncode không đồng nghĩa CanUpload/CanStart; hai giá trị sau
luôn false. Chưa kiểm thử SITL hoặc phần cứng cho luồng điều khiển.

## 1. Dữ liệu thực sự gửi cho FC

### 1.1 Nhận diện và khả năng hỗ trợ

Gateway phải phát hiện/đối chiếu UID flight controller, target_system,
target_component, firmware ArduPilot/PX4 và version, MAV_TYPE/airframe,
capabilities, hỗ trợ mission item INT và command cần dùng. Không lấy system ID
làm hardware identity, không tin firmware/type chỉ vì file kế hoạch khai như vậy.

### 1.2 Mission items qua MAVLink

| Trường wire | Đơn vị/ý nghĩa | Nguồn DroneOps |
| --- | --- | --- |
| target_system, target_component | ID FC đích | Connection đã xác thực + heartbeat |
| mission_type | MAV_MISSION_TYPE_MISSION = 0 | Adapter sinh |
| count | Tổng mission items trong MISSION_COUNT | Compiler sinh; không nhất thiết bằng số waypoint |
| seq | Thứ tự wire từ 0, uint16 | Compiler/adapter sinh, xử lý home theo firmware |
| frame | GLOBAL = 0 hoặc GLOBAL_RELATIVE_ALT = 3; MISSION = 2 cho command phi vị trí | altitudeReference + loại command |
| command | MAV_CMD: TAKEOFF 22, WAYPOINT 16, CHANGE_SPEED 178, LAND 21 | actionType + profile |
| current | 0 khi tạo upload candidate; không dùng để tự bắt đầu bay | Compiler sinh |
| autocontinue | 1 cho tuyến cơ bản | Profile được duyệt |
| param1..4 | float32; ý nghĩa theo command | Quyết định hành vi bên dưới |
| x, y | int32, WGS84 latitude/longitude × 10^7 trong MISSION_ITEM_INT | Tọa độ waypoint |
| z | float32, mét theo frame | Độ cao waypoint hoặc độ cao ground landing đã chọn |

MISSION_ITEM_INT vẫn dùng tọa độ E7 khi frame là 0/3. Các frame *_INT cũ đã
được supersede; hướng dẫn hiện tại khuyến nghị sender dùng frame không có hậu tố
_INT. Đừng nhầm cách encode với tên frame. [MAVLink Mission Protocol](https://mavlink.io/en/services/mission.html)

| Hành động | param1 | param2 | param3 | param4 | x/y/z |
| --- | --- | --- | --- | --- | --- |
| TakeOff | Pitch tối thiểu theo profile, copter preview dùng 0 | 0 | Flags 0 | Yaw cố định hoặc NaN theo policy | Vị trí, takeoff altitude |
| FlyThrough | Hold 0 giây | Acceptance radius m | Pass radius 0 cho profile tuyến cơ bản | Yaw | Vị trí/độ cao điểm bay |
| Hover | Hold time > 0 giây | Acceptance radius m | Pass radius 0 | Yaw | Vị trí/độ cao dừng |
| ChangeSpeed | Speed type = ground speed 1 | Cruise m/s | Throttle -1: giữ nguyên | 0 | Không dùng tọa độ |
| Land | Abort altitude 0: default profile | Precision land mode 0 | 0 | Yaw | Vị trí đáp và ground altitude theo frame |

Không dùng maxSpeed làm cruiseSpeed. Giới hạn maxSpeed cần đối chiếu/áp dụng
bằng profile/cấu hình FC hỗ trợ. Ground speed khác airspeed của fixed-wing.
Yaw policy Autopilot được biểu diễn null trong JSON preview rồi chuyển thành
NaN float32 khi encode MAVLink; không đổi null thành heading 0°.
NAV_WAYPOINT hold dùng cho multicopter, không suy rộng sang fixed-wing.
[Command definitions](https://mavlink.io/en/messages/common.html#mav_commands)

### 1.3 Home, terrain và cấu hình flight controller

- Home latitude/longitude/altitude MSL đã xác minh; planned home trong file chỉ
  dùng lập kế hoạch, không mặc nhiên là HOME_POSITION hiện tại.
- Relative altitude là so với home, không phải độ cao AGL theo địa hình.
- Ground landing altitude phải rõ ràng. Không biến altitude bay 30/50 m của
  waypoint cuối thành độ cao ground, cũng không mặc định mọi điểm đáp là 0 m.
- AGL cần terrain, firmware/capability phù hợp và dữ liệu còn mới; compiler v1
  chặn AGL thay vì đổi nó thành Relative.
- Kiểm tra tốc độ lên/xuống, gia tốc, tốc độ tối đa, RTL altitude, vùng bay và
  fence onboard. Những parameter này cần read-back và profile được duyệt;
  không tự ghi hàng loạt parameter từ web vào FC.
- Geofence/rally points, nếu bật, có upload/ACK/read-back riêng. Validation vùng
  hạn chế trên website không chứng minh fence đã được nạp vào FC.

### 1.4 Lệnh vận hành sau khi mission đã upload/verify

Command Protocol có target IDs, command, confirmation và param1..7, kèm
COMMAND_ACK và xác minh trạng thái thực. Các lệnh ứng viên: chọn mode mission,
MAV_CMD_MISSION_START (300), RTL/Hold/Land và ARM/DISARM (400) nếu giai đoạn
sau được duyệt. Adapter chọn trình tự và mode value theo firmware/version.

Đề xuất v1: pilot ARM thủ công sau khi tất cả kiểm tra sẵn sàng; hệ thống chỉ
start mission đã được xác nhận. Không ARM ngay khi bấm confirm T−5. Không gửi
thêm một GUIDED TAKEOFF nếu mission đã chứa TAKEOFF. Không force-arm, tắt
pre-arm checks hoặc gửi disarm trên không. ACK accepted chưa đủ: phải quan sát
mode, armed, landed/in-air và mission progression.
[Command Protocol](https://mavlink.io/en/services/command.html)

## 2. Dữ liệu gateway/backend giữ để cấp quyền thực thi

| Nhóm | Dữ liệu |
| --- | --- |
| Phiên và chủ thể | missionId, uavId, owner, UID, connectionId, lease, gateway instance |
| Kế hoạch | sourcePlanHash, compiledMissionHash, compiler/adapter version, options/policy version |
| Lệnh | commandId/idempotency key, issuedAtUtc, expiresAtUtc, expected prior state |
| Lịch | StartTime/EndTime UTC, UI GMT+7, ETA còn lại gồm quay về/đáp và margin |
| Xác nhận | Pilot consent cho đúng compiled hash + connection + policy, presence còn hiệu lực |
| Pin/tải | Pin đang lắp, capacity/config nguồn, %/mAh mới, payload thực tế, khối lượng cất cánh, endurance/model confidence |
| Kiểm tra | Approval hiện tại, preflight, home/terrain, fence, weather, FC pre-arm/health và read-back receipts |

Các trường này không phải waypoint parameters để FC tự hiểu. Gateway xác thực
và kiểm tra trước khi phát MAVLink. Lịch EndTime không phải một lệnh tự hạ cánh.
Xác nhận nguồn planHash hiện có chưa đủ để tự cho phép một compiler policy mới;
giai đoạn thực thi cần ràng buộc compiled hash và policy đã duyệt.

## 3. Luồng triển khai đề xuất

1. Chọn UAV/pin/payload, tạo và duyệt kế hoạch qua API hiện có.
2. Phát hiện firmware/airframe/home/capabilities; điền các input còn thiếu.
3. Compile, kiểm tra giới hạn và hash toàn bộ source + options + items.
4. Chỉ upload khi đúng UAV, đang landed và disarmed; không thay mission đang bay.
   Dùng MISSION_COUNT → REQUEST_INT / ITEM_INT → ACK, retry hữu hạn theo request.
5. Download/read-back, chuẩn hóa float32/NaN/home/sequence rồi so compiled plan.
   Nếu ArduPilot thêm/biểu diễn home khác, adapter phải xử lý trước bước verify.
6. T−5 đến trước EndTime: dùng notification/presence/confirm hiện có trong Live
   Monitoring. Kết nối trễ phải tính lại ETA hoàn thành trong thời gian còn lại.
7. Ngay trước start: kiểm tra lại toàn bộ evidence và consent. Pilot ARM thủ công
   khi sẵn sàng; thấy armed không tự được hiểu là đồng ý bay.
8. Phát start một lần với deadline. Chỉ chuyển Executing khi FC state xác nhận;
   upload ACK/command ACK không được dùng làm bằng chứng đã cất cánh.
9. Theo dõi tiến độ, failsafe và manual override. Mạng trở lại không tự resume
   AUTO hay phát lại takeoff/start. Giữ telemetry/recovery tới khi xác nhận đáp.

State dự kiến: Approved → Compiling → NeedsInput/Compiled → Uploading → Verified
→ AwaitingConfirmation → AwaitingManualArm → Starting → Executing → Returning/
Landing → Completed; lỗi trước bay → Blocked, ACK không rõ → StateUnknown cần
reconcile. Chưa thêm state persistence/uploader/start executor ở bước compiler này.

## 4. Failsafe để duyệt

Đây là chính sách **đề xuất cho sản phẩm**, chưa ghi parameter hoặc kích hoạt trên
FC. Giá trị cuối cùng phải theo firmware, airframe, vùng bay và kiểm thử.

| Tình huống | Trước cất cánh | Đang bay: đề xuất |
| --- | --- | --- |
| Sai UID, connection, hash hoặc policy | Block; yêu cầu chọn lại/confirm lại | Khóa lệnh mới, không đổi sang UAV khác; reconcile trạng thái |
| Thiếu/sai telemetry hoặc preflight | Block start | FC/gateway theo failsafe, web hiển thị mất dữ liệu; không đoán đã đáp |
| Mất Internet gateway–backend | Không cho start khi mất lease | Watchdog cục bộ: đề xuất 10 giây mất control lease → RTL nếu navigation/home hợp lệ; fallback hạ cánh theo FC |
| Pilot offline/tab ẩn quá presence TTL | Thu hồi khả năng start | Đề xuất hết presence 30 giây → recovery theo profile, không tự resume khi pilot quay lại |
| Gateway/Jetson/laptop crash hoặc mất MAVLink | Block | Failsafe onboard theo GCS loss; phải cấu hình/test heartbeat monitor trước chuyến bay |
| Mất RC | Block nếu profile yêu cầu RC | RTL khi navigation/home hợp lệ; fallback Land/Descend theo firmware. Pilot lấy lại điều khiển phải có thao tác rõ ràng |
| Pin thấp | Dưới nhu cầu + reserve hoặc dữ liệu thiếu → Block | Đề xuất Warn 30%, Return 25%, Critical Land 15%; so với năng lượng về nhà để Return sớm hơn. Không đủ pin về → phương án đáp dự phòng |
| GPS/EKF/global position không hợp lệ | Block | Không cố RTL bằng vị trí sai; dùng fallback position-loss onboard phù hợp khả năng định vị còn lại |
| Geofence/độ cao | Reject route không phù hợp | Chặn route từ trước + fence onboard có margin dừng; hành động Hold/RTL/Land được chốt theo khu vực và navigation, không force-disarm |
| Manual override hoặc mode thay đổi ngoài ý muốn | Hủy start đang chờ | Pilot nắm quyền; web ngừng auto-command, không giành lại AUTO |
| Upload thiếu/sai read-back | Hủy upload/start | Không upload/replace mission khi airborne |
| ACK mất, trễ hoặc lệnh trùng | commandId + deadline; kiểm tra state trước retry | Không resend ARM/TAKEOFF/START mù quáng. Reconcile với FC, trạng thái unknown không coi là thành công |
| Vượt ETA chặng/waypoint không tiến triển | Tính đủ thời gian trước start | Watchdog theo phase (takeoff/hover/landing có kỳ vọng riêng); recovery theo profile nếu quá margin |
| Gần EndTime/hết EndTime | Không start nếu ETA không còn đủ | Lên lịch quay về đủ sớm; EndTime mà còn bay → recovery, không cắt động cơ. Vẫn duy trì telemetry đến khi đáp |
| FC mất nguồn/hỏng nghiêm trọng | Block khi health check thất bại | FC không có nguồn không thể thực hiện lệnh; cần giải pháp phần cứng/nguồn phù hợp, không hứa xử lý bằng web |

30/25/15% là mốc ban đầu chờ duyệt, không thay thế voltage/cell checks, calibration
và tính pin để về/đáp. RTL altitude không có một giá trị chung cho mọi nơi: cần
home đúng, khoảng hở vật cản/địa hình và giới hạn vùng bay được kiểm tra.

### Vì sao phải có ba lớp

Backend chặn trước start và cấp command lease có deadline. Gateway dùng watchdog
cục bộ ngay cả khi Internet mất. FC giữ cấu hình failsafe onboard để làm việc
khi gateway cũng chết. Chỉ gửi RTL từ web khi Internet đã mất là không đủ.

GCS loss là mất heartbeat MAVLink, không tự động đồng nghĩa mất Internet. Nếu
Jetson vẫn gửi heartbeat vào FC khi cloud đã mất, GCS failsafe có thể không kích
hoạt. Cần thiết kế control lease/watchdog và nhận diện GCS đúng; thêm một GCS
khác không được vô tình che lỗi liên lạc cần giám sát. Hiện gateway telemetry
chưa có cơ chế điều khiển này; đây là phần cần triển khai sau duyệt.
[ArduPilot GCS failsafe](https://ardupilot.org/copter/docs/gcs-failsafe.html)

PX4/ArduPilot có cấu hình battery, RC/GCS, position và fence failsafe; tên/giá trị
parameter, exception modes và fallback không giống nhau. Các adapter cần đọc,
đối chiếu và test chứ không dùng chung một bảng số parameter.
[PX4 safety](https://docs.px4.io/main/en/config/safety),
[ArduPilot battery](https://ardupilot.org/copter/docs/failsafe-battery.html),
[ArduPilot RC](https://ardupilot.org/copter/docs/radio-failsafe.html).

## 5. Các điểm cần bạn duyệt

1. V1 dùng manual ARM, automatic start sau consent; chưa bật remote ARM.
2. Không chọn chế độ tiếp tục mission khi mất cloud/RC trong v1; ưu tiên recovery.
   Chấp thuận nguyên tắc RTL nếu navigation hợp lệ, fallback theo FC khi không đủ.
3. Chốt timeout control lease/presence và các mốc pin theo profile; giá trị đề
   xuất nêu trên cần SITL và thử nghiệm phần cứng, không ghi thẳng lên FC.
4. EndTime bao gồm ngân sách quay về/đáp; không tự bay bù nếu thiếu thời gian.
5. Khi có failsafe/manual override, không auto-resume; cần thao tác phục hồi rõ ràng.

## 6. API và kiểm thử

Chỉ mở rộng GET /api/missions/{id}/automation/plan bằng compilationPreviews cho
ArduPilot và PX4. Giữ các trường cũ, không thêm endpoint upload/start trùng chức
năng và không migration ở bước này. Options chưa có nguồn được duyệt nên preview
API mặc định báo NeedsInput; compiler vẫn được kiểm thử với bộ input đầy đủ.

Sau duyệt cần mở rộng evidence telemetry hiện có (home, firmware/capabilities,
landed state, EKF/pre-arm health, failsafe flags) và state/receipt/audit điều khiển.
Mọi thay đổi API/migration tiếp theo phải được báo riêng, tái sử dụng connection,
notification và validation hiện có. Không suy landed từ một giá trị armed=false.

Kiểm thử tiếp: SITL riêng ArduPilot/PX4 cho upload/read-back, consent expiry,
manual ARM/start, mất cloud/RC/MAVLink, pin thấp, GPS/EKF loss, fence, duplicate
commands, gateway reboot, ACK lost, deadline và recovery sau khi mạng trở lại.
Chỉ sau đó mới kiểm thử phần cứng có kiểm soát và xem xét bật dispatch.
