# Luồng nhiệm vụ bay tự động và dữ liệu cần có

Áp dụng cho profile multicopter `multicopter-envelope-v1`, ArduPilot/PX4.
Đây là tài liệu đối chiếu với code, không phải chứng nhận an toàn bay.

## Trạng thái triển khai

Đã có form tạo/sửa nhiệm vụ, xét duyệt, validate, dữ liệu AI, nhắc kết nối/xác
nhận, Live Monitoring, compiler MAVLink và logic ARM/đếm ngược. Luồng gửi lệnh
thực tế vẫn bị khóa: chưa có adapter áp dụng/đọc lại toàn bộ cấu hình FC và
chưa kiểm chứng failsafe trên SITL của cả hai firmware hoặc drone thật.
Không được coi một nhiệm vụ đã duyệt, một giá trị cache hay unit test là bằng
chứng drone đã được cấu hình để thực thi an toàn.

## Các bước trên website

1. Chọn rõ **Manual operation** hoặc **Automatic mission** ở đầu form. Form
   tạo mới không tự chọn chế độ. Manual dùng RC/GCS; website không tự khởi
   động chuyến bay trong chế độ này.
2. Chọn UAV đã đăng ký, tên, lịch theo giờ Việt Nam GMT+7 và vẽ route. Automatic
   dùng độ cao Relative theo Home của FC; đầu route là TakeOff, cuối là Land.
3. Với Automatic, dùng giới hạn được điền sẵn hoặc sửa trong phạm vi cho phép.
   Thông số mở rộng của mỗi waypoint nằm ngay dưới waypoint đó. Hover mở sẵn
   phần nhập thời gian dừng; Land mở phần cao độ mặt đất hạ cánh.
4. Gửi qua API tạo/sửa nhiệm vụ hiện có. Backend validate độc lập; AI chỉ hỗ
   trợ phân tích. Admin/manager xem route, envelope và thay đổi waypoint trước
   khi duyệt. Sửa kế hoạch/giới hạn làm thay đổi hash và cần duyệt/xác nhận lại.
5. Kết nối đúng FC đã gắn hardware identity với UAV/nhiệm vụ. Gateway đọc
   telemetry, firmware, Home và các tham số hỗ trợ. Chỉ upload khi FC disarmed,
   ở mặt đất và adapter chứng minh được cấu hình giới hạn đúng kế hoạch.
   Hiện bước xác minh adapter còn là điều kiện chặn thực thi.
6. Từ StartTime − 5 phút đến trước EndTime, người dùng mở Live Monitoring,
   giữ tab online và xác nhận sau khi dữ liệu/preflight sẵn sàng. Confirm
   chuẩn bị không tự ARM và không tự cất cánh.
7. Chỉ từ StartTime mới cho ARM, khi còn trong thời gian nhiệm vụ và mọi điều
   kiện đã đạt. Phải có ACK thành công cùng heartbeat xác nhận ARMED từ FC
   mới bắt đầu đếm 10 giây. Không đếm từ thời điểm bấm nút. Hết 10 giây chỉ
   gửi Start nếu điều kiện còn hợp lệ. Có nút hủy đếm ngược.
8. Hủy cất cánh không tự disarm. Người dùng có thể DISARM khi FC xác nhận đang
   ở mặt đất, trong trạng thái được phép. Không force ARM/DISARM; không DISARM
   trên không. Mất kết quả ACK không tự gửi lại lệnh có kết quả chưa biết.
9. Sau EndTime không gửi nhắc/confirm/ARM/Start mới. Hết lịch khi drone đang
   trên không không được dùng làm lý do tắt động cơ; xử lý thu hồi phải do
   cấu hình onboard đã kiểm chứng. Phần thu hồi này chưa hoàn tất adapter.

## Người dùng bắt buộc nhập khi tạo

| Thông tin | Yêu cầu |
| --- | --- |
| Chế độ Manual/Automatic | Phải chủ động chọn |
| UAV | UAV active được phân công cho người tạo |
| Tên nhiệm vụ | Bắt buộc trong DroneOps để quản lý/duyệt |
| StartTime, EndTime | Bắt buộc; bắt đầu trong tương lai, kết thúc sau bắt đầu; UI GMT+7, lưu thời điểm UTC |
| Route | Tối thiểu TakeOff và Land; tọa độ hợp lệ, độ cao và action mỗi điểm; thứ tự do UI tạo/sắp xếp |
| Altitude reference | Automatic bắt buộc Relative; không tự coi Relative là độ cao so với địa hình |
| Hold time | Chỉ bắt buộc khi waypoint là Hover, lớn hơn 0, không quá 600 giây |
| Điểm hạ cánh dự phòng | Không bắt buộc đề xuất; nếu đánh dấu thì bắt buộc cao độ mặt đất và ghi chú clearance tối thiểu 10 ký tự |
| Tốc độ lập kế hoạch Manual | Bắt buộc trong hợp đồng API hiện có; không phải lệnh điều khiển tốc độ RC |

Độ cao route hiện dùng quy tắc chung **>10 m**; Automatic còn phải nằm dưới
ceiling đã chọn. Độ cao endpoint Land của route không thay cho cao độ mặt đất
hạ cánh. Compiler dùng trường ground elevation riêng khi tạo lệnh LAND.

## Tùy chọn khi tạo, có chính sách mặc định

| Thông tin | Mặc định / giới hạn hiện có |
| --- | --- |
| Cruise và speed ceiling | Automatic mặc định 3 m/s (10,8 km/h); cruise ≥0,5 và ≤ ceiling; ceiling ≤5 m/s |
| Preset ≤10 km/h | 2,77 m/s cho cả cruise và ceiling; đây là mục tiêu, không bảo đảm không có overshoot thực tế |
| Takeoff / climb | 1 / 1 m/s; takeoff 1–1,5; climb ≥ takeoff và ≤1,5 |
| Descent / landing | 0,7 / 0,7 m/s; descent 0,6–1; landing 0,6–0,7 và ≤descent |
| Ceiling | 30 m Relative; cho giảm xuống >10 m; waypoint mới thường 20 m hoặc ceiling thấp hơn |
| Giới hạn dự báo gió / gust | 3 / 5 m/s; có thể giảm; gust ≥ gió |
| Mô tả nhiệm vụ | Không bắt buộc |
| Tốc độ riêng từng waypoint | Không bắt buộc; nếu nhập phải ≥0,5 và ≤ ceiling tốc độ nhiệm vụ; không áp dụng tốc độ ngang riêng cho TakeOff |
| Hold ở FlyThrough | Không bắt buộc; bỏ trống là không dừng |
| Acceptance radius | Không bắt buộc nhập; >0 và ≤10 m nếu nhập; nếu trống phải có FC default hỗ trợ trước compile/upload |
| Yaw | Không bắt buộc; bỏ trống theo FC; nếu nhập 0≤yaw<360° |
| Ghi chú điểm Land | Tùy chọn nếu không đề xuất là điểm khẩn cấp |

Pass radius khác 0, camera/gimbal, action MAV_CMD tùy ý và kết thúc route bằng
RTL chưa được hỗ trợ trong profile này. Không âm thầm bỏ qua giá trị không hỗ
trợ. Không tự đổi route đã nhập khi người dùng chuyển chế độ; nếu điểm 50 m
vượt ceiling Automatic 30 m, form yêu cầu sửa rõ ràng.

## Không cần nhập khi tạo nhưng bắt buộc có trước bay

| Dữ liệu / bằng chứng | Nguồn và cách xử lý nếu thiếu |
| --- | --- |
| Hardware UID đúng UAV/nhiệm vụ | FC/gateway xác thực; không dùng IP làm định danh; thiếu hoặc sai thì chặn |
| Firmware, version, loại phương tiện | FC; phải phù hợp adapter, không đoán theo nhãn UAV |
| Home và trạng thái navigation | HOME_POSITION/telemetry FC còn mới; không thay Home bằng điểm click đầu tiên |
| ARMED/DISARMED, ON_GROUND, pre-arm readiness | FC còn mới; ACK và heartbeat dùng xác minh, không dựa riêng UI |
| Pin hiện tại | Telemetry FC: phần trăm, voltage/current/consumed nếu FC hỗ trợ; không suy ra dữ liệu không có |
| Capacity/nominal voltage, mô hình năng lượng | Hồ sơ pin + dữ liệu đo/hiệu chỉnh; cần để ước lượng Wh; max voltage không thay nominal voltage |
| Cao độ mặt đất điểm Land | Trường landing ground altitude khi lập kế hoạch hoặc cấu hình FC mission trước bay; không gán từ độ cao bay |
| Tham số limits/failsafe đã áp dụng | Adapter đúng firmware phải đọc lại từ FC và gắn receipt với compiled hash; cache lập kế hoạch không đủ |
| Mission trên FC đúng kế hoạch | Upload ACK và đọc lại các mission item/hash; thay đổi từ GCS khác làm mất xác minh |
| Dự báo thời tiết mới, khu vực bay, validation | Backend hiện có; thiếu/thất bại không được đánh dấu Pass |
| Duyệt, consent, presence | Kế hoạch đã duyệt không đổi; confirm còn hợp lệ; đúng connection; tab Live Monitoring online |

Mô hình pin hiện tại vẫn là ước lượng chưa hiệu chỉnh. Chưa có bằng chứng năng
lượng để tự động chọn tuyến cứu hộ; chức năng bay thực tế tiếp tục bị khóa.
Max pack voltage được lưu riêng, không đổi nghĩa trường nominal cũ. Tải trọng
và hiệu suất động lực cần có trong hiệu chỉnh; không thể tính chắc khả năng
quay về chỉ từ mAh và phần trăm pin.

## Thông báo và xác nhận

| Tình huống | Xử lý đã có |
| --- | --- |
| Từ T−5 phút, gateway chưa kết nối | Gửi nhắc kết nối một lần cho nhiệm vụ, dẫn đến Live Monitoring |
| Đã kết nối nhưng chưa có telemetry/preflight đạt | Chưa mời confirm dựa trên dữ liệu thiếu |
| Đủ dữ liệu, kết nối đúng, chưa confirm | Gửi nhắc confirm; giới hạn một lần mỗi 2 phút |
| Kết nối trong cửa sổ T−5 đến EndTime | Kiểm tra lại ngay; mời confirm khi telemetry/preflight sẵn sàng |
| Kết nối trễ sau StartTime nhưng trước EndTime | Vẫn cho chuẩn bị/xác nhận khi đủ điều kiện; không tự cất cánh bù lịch |
| Mission đã End/không còn Approved | Không gửi nhắc mới, không cho confirm/ARM/Start mới |
| DISARMED | Trạng thái/nhắc ARM nằm trong Live Monitoring, không tạo thông báo chuông riêng |

## Failsafe để duyệt và kiểm chứng

Các ngưỡng dưới là **chính sách thử nghiệm của ứng dụng**, không phải bộ tham
số an toàn chung do Mission Planner/QGroundControl bảo đảm. Logic quyết định
đã có test; cấu hình và thực thi tương ứng trên FC **chưa hoàn tất**.

| Trigger / điều kiện | Quyết định và ưu tiên |
| --- | --- |
| Đang ở mặt đất | InhibitLaunch; không tự ARM/force-disarm |
| FC báo pin critical hoặc pin ≤15% | Ưu tiên quy trình hạ cánh khẩn cấp native đã kiểm chứng; không bắt đầu chặng RTL mới |
| Pin ≤30%, mất điều khiển hoặc năng lượng không đủ về Home | Chỉ về Home nếu navigation, clearance và năng lượng đến hạ cánh + reserve đều được xác minh |
| Home không xác minh là reachable | Chọn điểm hạ cánh dự phòng gần nhất trong tập đã duyệt, đã xác minh clearance và đủ năng lượng đến hạ cánh; không chọn waypoint thường |
| Không có điểm nào đủ bằng chứng reachable | Dùng quy trình emergency native đã cấu hình; không tạo tuyến giả |
| Mất GPS/navigation, telemetry quá 3 giây hoặc pin không có dữ liệu hợp lệ | Không cho website phát lệnh return mới; dựa fallback onboard đã kiểm chứng |
| Vượt tốc độ ngang | Phát hiện khi >max + max(0,5 m/s,20% max), liên tục ≥2 giây; HoldAndReassess nếu không có lỗi ưu tiên cao hơn |
| Vượt tốc độ lên/xuống | Theo pha takeoff/climb/landing/descent, >giới hạn tương ứng +0,3 m/s, ≥2 giây; cùng nguyên tắc HoldAndReassess |
| Vượt ceiling | >ceiling +2 m, ≥2 giây; cùng nguyên tắc trên |
| Nhiều lỗi đồng thời | Pin critical ưu tiên hơn return/mất điều khiển/overspeed; thiếu navigation chặn tuyến mới; không DISARM trên không |
| Link phục hồi khi đang thu hồi | Không tự resume mission; tiếp tục đánh giá năng lượng; RTL có thể đổi sang điểm khả thi khi pin giảm |
| Đã vào pha hạ cánh thu hồi | Không ngắt bằng một lệnh RTL mới chỉ vì mất link |
| Browser/gateway mất trong lúc ARM/countdown | Thu hồi quyền Start, hủy countdown theo lease; không replay lệnh có kết quả chưa biết; không bảo đảm disarm nếu không có link |

Tolerance/dwell chỉ dùng lọc nhiễu khi phát hiện vi phạm, không cho phép gửi
mục tiêu cao hơn giới hạn duyệt. Chưa thể bảo đảm tốc độ thực tế không bao giờ
vượt mục tiêu vì gió/quán tính/lỗi điều khiển. Pin cạn vật lý hoặc mất nguồn
không thể được phần mềm bảo đảm hạ cánh an toàn.

Policy contract có landing reserve 20%, nhưng thuật toán routing yêu cầu giá
trị reserve Wh dương cùng mô hình đã hiệu chỉnh; chưa có adapter thực thi để
coi 20% là bằng chứng đủ pin. RC loss và GCS/cloud loss phải được phân biệt
và cấu hình native theo từng firmware trước khi phát hành bay thật.

## Dữ liệu gửi FC và thay đổi API

FC nhận MAVLink mission items/parameters/commands, không nhận nguyên file
`.plan` JSON như website lưu. Compiler hiện tạo thứ tự item, MAV_CMD, frame,
param1–4, latitude/longitude E7, altitude m, autocontinue; có DO_CHANGE_SPEED
và các lệnh TakeOff/Waypoint/Land được hỗ trợ. Envelope gồm các giới hạn đã
duyệt; gửi và xác minh parameter riêng với upload mission là hai bước khác nhau.
User consent/online status và lịch thuộc điều phối ứng dụng, không thay cho
mission item hay failsafe onboard.

Dùng lại endpoint mission/automation/UAV hiện có. Không tạo API trùng chức
năng. Các trường bổ sung đã có: `automaticFlightLimits`, waypoint `navigation`,
UAV `batteryMaxVoltage` và `firmwareNavigationDefaults`; gateway evidence thêm
firmware parameters/Home. Lần cập nhật form này bổ sung `navigation` vào từng
waypoint của **response GET automation/plan hiện có**, để người dùng kiểm tra
đủ tùy chọn; request/URL không thay đổi. Không cần migration mới cho thay đổi này.

## Những việc còn chặn phát hành bay thật

- Adapter versioned cho ArduPilot/PX4: áp dụng/read-back limits, geofence,
  RC/GCS/navigation/battery fallback và recovery destinations.
- Mô hình năng lượng hiệu chỉnh; xác minh địa hình, vật cản, điểm hạ cánh.
- SITL từng firmware và lỗi kết hợp; thử FC/drone thật có giám sát.
- Chỉ sau khi vượt các kiểm tra trên mới cân nhắc bật execution flags.

Tài liệu tham số và nguồn chính thức: [Operating envelope](automatic-flight-operating-envelope.md).
