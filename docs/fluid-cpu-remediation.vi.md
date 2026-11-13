# Giảm Fluid Active CPU

## Trạng thái xác minh production

Ngày 10/11/2026: truy vấn chỉ đọc Vercel API bằng phiên CLI hiện có trả về HTTP 403.
Chưa xác nhận được commit đang chạy, biến môi trường hay số monitor thực tế; chưa deploy thay đổi.
Repo đặt `Monitoring__Enabled=false` và `Monitoring__DedicatedProcessRequired=true` trong Docker image.
Chỉ tiến trình có `--monitor-only` bật workers khi yêu cầu dedicated process được bật.
`vercel.json` cấu hình đúng một monitor tại `icn1`; cần đối chiếu dashboard và các deployment cũ.

Sau deploy, kiểm tra `/health` của API trả `role=api`, `monitoringEnabled=false`,
`commit` khớp commit đã deploy (nếu Vercel cung cấp `VERCEL_GIT_COMMIT_SHA`).
Log khởi động của daemon phải báo `role=monitor`, `monitoringEnabled=true`.
Kiểm tra Vercel Environment Variables không ghi đè `Monitoring__DedicatedProcessRequired=false`.
Không xem cấu hình repo là bằng chứng production đã áp dụng.

## SSE

Mỗi kết nối được xác thực và kiểm tra quyền mission trước khi đăng ký snapshot.
Các kết nối cùng mission trong cùng API process dùng chung một lần đọc DB trong mỗi
khoảng 1,5 giây. Mỗi lần đọc dùng DI scope riêng và được tuần tự hóa bằng semaphore.
Cache chỉ tồn tại khi có người xem; kết nối cuối cùng đóng sẽ giải phóng entry.
Lỗi hoặc hủy một lần đọc không giữ semaphore hay làm hỏng các subscription khác.
Mission khác nhau và API instance khác nhau không dùng chung cache.

Snapshot đầu tiên chứa incidents hiện có. Các lần sau chỉ truy vấn incidents đổi gần đây,
với overlap hai giây; từng kết nối so sánh nội dung để không phát lại sự kiện giống nhau.
Telemetry vẫn được kiểm tra mỗi 1,5 giây để phát chuyển trạng thái Stale/Offline dù không có gói mới.

## Workers

Kiểm duyệt ảnh và hậu chuyến bay tăng khoảng nghỉ theo cấp số nhân khi không tìm được việc,
tối đa 60 giây. Khi xử lý được việc hoặc gặp lỗi, quay lại khoảng nghỉ cơ sở (mặc định 5 giây).
Việc mới xuất hiện trong lúc worker đang nghỉ có thể chờ tối đa khoảng 60 giây trước lần quét tiếp theo.
Monitor mất kết nối (1 giây), preflight/reminders (10 giây), polling điều khiển bay (1 giây)
và pilot presence giữ nguyên.

## Đánh giá sau deploy

So sánh Active CPU theo route/process trong cùng thời lượng, số gateway và số tab.
Với N tab cùng mission trên cùng instance, số lần đọc snapshot mỗi chu kỳ giảm từ N xuống 1;
đây không phải cam kết CPU giảm cùng tỷ lệ. Theo dõi lỗi SSE, chuyển Stale/Offline,
thời gian xử lý queue và số tiến trình monitor trước khi kết luận hiệu quả.
