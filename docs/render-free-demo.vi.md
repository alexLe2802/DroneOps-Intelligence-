# Chuyển backend demo sang Render Free

## Cấu hình đã chuẩn bị

`render.yaml` tạo đúng một Docker web service ở Singapore, plan Free, cổng 8080,
health check `/health`. Không tạo database mới hoặc worker trả phí.
API, WebSocket gateway, SSE và monitors chạy chung tiến trình.
Riêng service demo ghi đè `Monitoring__DedicatedProcessRequired=false` và
`Monitoring__Enabled=true`; giữ nguyên mặc định Docker an toàn cho các deployment khác.
Không chạy thêm daemon `--monitor-only` cho cùng database.

Frontend đã có cấu hình Cloudflare tại `fe/cloudflare.config.ts`. File này chưa chứng minh
frontend đang hoạt động trên Cloudflare; cần kiểm tra domain thật trước khi chuyển backend.

## Các giới hạn đã chấp nhận

- Service ngủ sau 15 phút không có inbound traffic; thức dậy thường mất khoảng một phút.
- Mọi monitor dừng khi service ngủ. Không dùng cấu hình này cho vận hành bay thật cần giám sát liên tục.
- Workspace có 750 giờ Free mỗi tháng dùng chung cho các free web service.
- SMTP 25/465/587 bị chặn. Gmail SMTP hiện tại không gửi OTP/reset password được trên Free.
  Tài khoản demo đã tồn tại có thể dùng luồng đăng nhập hiện có nếu luồng đó không yêu cầu email.
  Muốn demo các chức năng email phải tích hợp nhà cung cấp qua HTTPS; không bỏ xác minh hay giả gửi email.
- Filesystem tạm thời: tiếp tục dùng database/storage hiện có, không lưu dữ liệu cần giữ trên container.
- Có thể bị suspend khi hết quota; không bật gói trả phí hoặc thêm billing ngoài ý muốn.

Nguồn: https://render.com/docs/free và https://render.com/docs/websocket

## Tạo service

1. Kết nối Render với cuộc trò chuyện hoặc đăng nhập https://dashboard.render.com/.
2. Đưa các thay đổi đã review lên repository GitHub `alexLe2802/DroneOps-Intelligence-`.
   Tạo Blueprint từ repo và chọn đúng branch/commit chứa `render.yaml` cùng các sửa CPU.
   Chưa push/commit hoặc tạo service từ phiên làm việc này.
3. Kiểm tra plan **Free**, region **Singapore**, đúng một service; không thêm database/worker.
4. Nhập các secrets mà Blueprint yêu cầu từ cấu hình deployment hiện có, không đưa secrets vào Git.
   Giữ nguyên JWT key/issuer/audience và integration credentials nếu muốn giữ tương thích.
   Nếu có cấu hình `IntegrationSettings__Sources__...` ở production, sao chép đầy đủ mapping đó.
5. Connection string cần truy cập được từ Render và dùng đường dẫn certificate Linux:
   `Root Certificate=/app/database/certificates/prod-ca-2021.crt`.
   Không dùng đường dẫn ổ D: hay localhost. Giữ SSL verification phù hợp kết nối hiện có.
6. Bổ sung các cấu hình production còn lại (AI model, thresholds, storage và mappings) nếu đã override;
   `.dockerignore` loại appsettings JSON khỏi image, không trông chờ giá trị local tự xuất hiện.
7. Deploy thủ công. Kiểm tra URL thực tế do Render cấp; không giả định tên domain luôn khả dụng.

## Kiểm tra trước khi chuyển traffic

- `GET https://<render-host>/health` phải trả `status=ok`, `role=api`, `monitoringEnabled=true`.
  `commit` lấy từ `RENDER_GIT_COMMIT`; đối chiếu với commit trong dashboard Render.
  Nếu nền tảng không cung cấp biến này, giá trị có thể null và cần kiểm tra commit ở dashboard.
- Kiểm tra đăng nhập bằng tài khoản demo, quyền truy cập mission, SSE và WebSocket gateway.
- Xác nhận các schema hiện tại đã được migrate; startup checks thất bại phải được xử lý trước cutover.
- Bảo đảm monitor deployment cũ không còn chạy trên cùng database trước khi bật monitor mới.
- Không đổi DNS khi service chưa qua các kiểm tra trên.

## Chuyển domain và gateway

Ưu tiên gắn custom domain `api.droneops.click` trong Render và cập nhật DNS theo hướng dẫn Render
sau khi health/login/SSE/WebSocket thành công. Cách này giữ `API_BASE_URL` của frontend và
backend origin của gateway nếu chúng đang dùng custom domain này.

Nếu dùng domain Render mới: cập nhật `API_BASE_URL` trong frontend Cloudflare rồi deploy lại frontend;
cập nhật backend origin gateway bằng luồng cấu hình/pairing hiện có. URL WebSocket dùng `wss://`.
Đối chiếu redirect/origin trong các tích hợp xác thực nếu domain frontend cũng thay đổi.
Không sao chép hay hiển thị token ghép nối trong logs.

Rollback DNS/frontend origin về host cũ chỉ khi host đó đã hoạt động trở lại. Vercel đang pause
không phải một đích rollback sẵn sàng.

## Ngừng Vercel

Repo đã đặt `git.deploymentEnabled=false` trong `vercel.json` để ngăn deploy tự động qua Git
sau khi thay đổi được push vào branch tương ứng. Cờ này không shutdown deployment/daemon đang chạy,
không ngăn deploy thủ công, và chưa có hiệu lực remote khi thay đổi vẫn chỉ ở local.
Sau khi Render hoạt động, cần dùng quyền quản trị Vercel để pause project/daemon và ngắt liên kết Git.
Phiên CLI đã thử truy vấn Vercel trước đó trả 403 nên chưa thể thực hiện thao tác shutdown remote.
Không xóa project hay domains trước khi kiểm tra Render và DNS; giữ frontend Cloudflare nguyên trạng.

## Tự deploy khi merge vào main

`render.yaml` đặt `branch: main` và `autoDeployTrigger: commit`: mỗi commit được push/merge
vào `main` sẽ kích hoạt deploy backend. CI GitHub vẫn chạy riêng. Chế độ này không chờ
check Vercel, hiện đang thất bại do tài khoản Vercel bị chặn.

- Nếu service được quản lý bằng Blueprint: đồng bộ Blueprint với `render.yaml` mới.
  Kiểm tra Blueprint có bật tự đồng bộ khi repo thay đổi.
- Nếu service được tạo trực tiếp: Render Dashboard → `droneops-api-demo` → Settings →
  Build & Deploy → Branch = `main`, Auto-Deploy = **On Commit**, rồi lưu.
  Chỉ thay file YAML không cập nhật một service không được quản lý bằng Blueprint.
- Để triển khai ngay bản đã merge trước khi bật tự deploy: **Manual Deploy → Deploy latest commit**.
- Kiểm tra `/health` trả commit trùng `main`; merge thành công chưa chứng minh deploy đã xong.

Để ngừng check **Vercel — Account is blocked**, vào từng Vercel project liên kết repo này →
Settings → Git → **Disconnect**. `git.deploymentEnabled=false` đã có trong repo;
nếu Vercel vẫn gửi status thì cần ngắt tích hợp ở dịch vụ, không sửa workflow CI.
Nếu không truy cập được Vercel, GitHub → Settings tài khoản → Applications →
Installed GitHub Apps → Vercel → Configure → bỏ quyền truy cập **chỉ repo này**
(nếu đang chọn All repositories, đổi sang danh sách vẫn giữ các repo khác cần dùng).
Check cũ có thể vẫn còn trong lịch sử commit. Nếu ruleset/branch protection yêu cầu check
Vercel thì bỏ riêng yêu cầu đó; giữ các check CI của dự án.

Tham khảo: https://render.com/docs/blueprint-spec và https://vercel.com/docs/git/vercel-for-github
