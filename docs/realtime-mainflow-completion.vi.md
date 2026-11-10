# Real-Time Monitoring & Incident Management — phạm vi một drone

Ngày cập nhật: 10/11/2026. Thay đổi mã nguồn và đặc tả; không chạy test,
không kết nối/ARM drone, không áp dụng migration hoặc triển khai trong lượt này.

## Luồng hoàn chỉnh

1. Operator tạo mission Automatic, UAV đã đăng ký, lịch bay và tuyến TakeOff →
   FlyThrough/Hover → Land; Manager duyệt đúng nội dung. Pending proposal chặn dispatch.
2. Gateway xác thực đúng UID/connection; firmware runtime hiện là ArduCopter 4.7.1.
   Compiler giữ mapping waypoint → command, kể cả home/speed/approach phụ.
3. Gateway xác minh envelope, upload và canonical read-back. Pilot xác nhận trong
   [T−5 phút, start), giữ tab online/visible; FC ở LOITER và pre-arm đạt.
4. Scheduler kiểm tra readiness/presence/preflight/hash, lưu một dispatch identity,
   phát ARM rồi START trong deadline start + 5 giây. Không có catch-up hoặc replay
   khi bị Blocked/Expired/Unknown. Command ID được gateway ghi bền vững trước side effect.
5. ACK không tạo chuyến bay. Đúng uploaded hash, armed + IN_AIR + mission mode mới
   tạo MissionFlightExecution, snapshot và mapping bất biến; mission thành InProgress.
6. MISSION_ITEM_REACHED cập nhật các waypoint thực sự đạt. Phần trăm lấy số planned
   waypoint đã đạt chia số waypoint trong execution snapshot, không dùng MISSION_CURRENT.
7. Hai quan sát landed/disarmed khác nhau sau khi đã bay xác lập terminal evidence.
   Đạt các waypoint trước LAND và đang ở terminal command: Completed/100%; thiếu
   bằng chứng toàn tuyến: Aborted, giữ tiến độ và telemetry thực. Một terminal event,
   một report Queued; worker xử lý Available/Failed độc lập. Quá EndTime không tự Completed.
8. Telemetry có source event/boot identity, source observation time và timestamp riêng
   cho battery/altitude/speed. Cached measurement không trở thành hai lần đo độc lập.
9. Incident recovery kết thúc abnormal episode; Manager vẫn là người resolve nghiệp vụ.
   Severity escalation cập nhật cùng incident và thêm frozen evidence vào timeline.
   Manager sửa detector qua Incidents → Detector thresholds; mỗi lần tạo revision bất biến.
10. Web dùng snapshot + SSE; tự tính freshness khi mất stream, ngừng retry khi 401/403/404,
    giữ last-known. Incident polling lấy full snapshot cho một drone, không dùng cursor
    thời gian có thể bỏ sót commit muộn. Unread alerts có nguồn lưu bền vững riêng.

## Chuẩn bị triển khai

Áp dụng hai migration telemetry mới theo `database/migration-order.txt`, hoặc API
`--migrate-telemetry`: `20261110000100_realtime_episodes.sql` và
`20261110000200_realtime_execution.sql`. Startup kiểm tra schema trước khi mở worker.
Không tự sửa dữ liệu completion lịch sử từng được tạo bởi schedule monitor; cần
đối chiếu bằng chứng riêng trước khi sửa dữ liệu cũ.

Công tắc backend `FlightExecution__Enabled` và gateway `--enable-flight-control`
phải được người vận hành bật có chủ đích. Mặc định không phát lệnh bay. Không tắt
arming checks, không force-arm, không DISARM trên không. RC/failsafe onboard giữ thẩm quyền.
Legacy completion HTTP mặc định tắt; legacy telemetry cần allowlist SourceId/UAV.
Ngưỡng MSL phải nhập theo khu vực thực tế, không tự đặt độ cao địa hình/Home.

## Bài nghiệm thu còn lại

Chỉ giữ một task Jira nghiệm thu active cho luồng này: KAN-51 / E2E-DRONE-01.
Một drone, một hành trình nhiều waypoint, một hoặc hai phiên Operator/Manager.
Không yêu cầu đồng thời năm drone hoặc chạy 60 phút. Thời lượng theo hành trình;
script telemetry smoke tùy chọn mặc định 180 giây. Không thay đổi các kiểm tra quyền,
UID, hash, replay và bằng chứng cất/hạ cánh để làm test “dễ đạt”.

Chi tiết nghiệm thu ở Jira KAN-51. Report5 đã nộp được giữ nguyên. Trạng thái Not run;
không ghi Pass hay xác nhận bay thật khi chưa có biên bản/log của người vận hành.

## Cập nhật luồng tạo nhiệm vụ — bỏ bước Home

Theo yêu cầu mới, form tạo/sửa không cho chọn Planned Home và không yêu cầu trường này.
TakeOff là waypoint đầu tiên, không bị cộng số thứ tự cho Home. Gateway bỏ đối chiếu
Planned Home với vị trí drone/Home FC; Home thực tế và kiểm tra TakeOff của FC vẫn giữ nguyên.
Không thay đổi Report5 hay dữ liệu Home thực tế trên drone.
