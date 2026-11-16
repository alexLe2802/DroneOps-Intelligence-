# Tự động triển khai frontend lên Cloudflare

Workflow `CI` trong `.github/workflows/ci.yml` chạy job `Deploy Cloudflare production` sau khi `PR Quality Gate` thành công trên mỗi push vào `main`. Pull request chỉ chạy kiểm tra, không deploy production.

Đích triển khai giữ nguyên từ `fe/cloudflare.config.ts`:

- Worker: `droneops-web`.
- Domains: `https://droneops.click`, `https://www.droneops.click`.
- Backend: `https://api.droneops.click`.

## Cấu hình một lần

1. Trong Cloudflare, mở **My Profile → API Tokens → Create Token**. Chọn template **Edit Cloudflare Workers**.
2. Giới hạn tài khoản chứa Worker và zone `droneops.click`. Token cần quyền sửa/deploy Workers và sửa Workers Routes cho zone có custom domain. Không dùng Global API Key.
3. Trong GitHub repository, mở **Settings → Secrets and variables → Actions → New repository secret**.
4. Tên secret: `CLOUDFLARE_API_TOKEN`. Dán token trực tiếp vào trường secret; không đưa token vào commit, log hoặc chat.
5. Merge thay đổi workflow vào `main`. Nếu run đầu tiên thất bại vì secret chưa có, thêm secret rồi chạy **Re-run failed jobs** trên run của commit mới nhất.

Hướng dẫn chính thức: [Cloudflare Workers — GitHub Actions](https://developers.cloudflare.com/workers/ci-cd/external-cicd/github-actions/).

## Luồng mỗi lần cập nhật main

1. CI kiểm tra các phần bị ảnh hưởng.
2. Job deploy checkout chính xác SHA đã qua CI, chạy `npm ci`, rồi build production bằng `npm run build:vinext` với `DRONEOPS_CI=false`. Không sử dụng artifact kiểm thử có localhost bindings.
3. Ngay trước deploy, kiểm tra SHA mới nhất trên `main`; bỏ qua run đã bị commit khác thay thế.
4. `cf deploy --prebuilt` đưa Worker và assets lên domain, gắn tag/message chứa SHA. Token chỉ được cấp cho bước kiểm tra credential và bước deploy.
5. Kiểm tra landing và login trên cả hai domain trả HTTP 200. Kết quả và SHA nằm trong job summary. Các kiểm tra này không chứng minh luồng đăng nhập và nghiệp vụ đã được nghiệm thu.

Các job deploy được tuần tự hóa, không hủy giữa chừng. Backend Render, Supabase migrations và gateway Jetson không được triển khai bởi job này.

## Khi có lỗi

- **Thiếu token:** thêm repository secret rồi chạy lại job.
- **Token verification thất bại:** GitHub đang giữ giá trị không được Cloudflare chấp nhận. Kiểm tra đã cập nhật secret sau khi tạo token mới; chỉ dán giá trị token, không dán tên/ID, lệnh curl, dấu nháy hoặc tiền tố Bearer. Kiểm tra hạn dùng và IP filtering.
- **Token active nhưng không đọc được Worker:** đối chiếu Account resources với accountId trong `fe/cloudflare.config.ts` và quyền Workers Scripts/Edit. Quyền DNS/Edit đơn lẻ không cho phép deploy Workers.
- **Cloudflare 403:** kiểm tra account/zone scope và quyền Workers/Workers Routes của token.
- **Quality Gate thất bại:** sửa CI trước; job deploy sẽ không chạy.
- **Run bị thay thế:** xem run của commit mới nhất trên `main`.
- **Deploy lỗi hoặc public smoke check lỗi:** xem log và deployment đang phục vụ trong Cloudflare. Nếu cần khôi phục, chọn deployment trước đó trong Cloudflare; workflow không tự rollback về phiên bản cũ khi có commit mới hơn.

Bước `Verify Cloudflare token and Worker account access` chỉ gọi API đọc: xác minh token và truy cập Worker đúng account. Nó không in token/response body và không chứng minh token có đủ quyền ghi. Chạy lại job của commit cũ sẽ dùng workflow cũ, chưa có bước chẩn đoán này.
