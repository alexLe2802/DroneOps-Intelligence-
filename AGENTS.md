# Hướng dẫn làm việc trong DroneOps Intelligence

Áp dụng cho toàn repo. Đọc thêm `AGENTS.md` trong thư mục con trước khi sửa code ở đó, đặc biệt `fe/AGENTS.md`. Hướng dẫn hệ thống và yêu cầu trực tiếp của người dùng có ưu tiên cao hơn file này.

## Code Review Rules

Áp dụng khi Codex Code Review tích hợp GitHub review PR. Review chỉ đọc, không sửa file, migrate, deploy hoặc thực thi code/script không tin cậy từ PR. Các kiểm tra build/lint/test xác định nằm trong `PR Quality Gate`; không yêu cầu chạy lại chúng chỉ để tạo nhận xét review.

- **Luồng nghiệp vụ và evidence:** Đối chiếu thay đổi với SRS và phiếu đăng ký hiện hành nêu ở mục 1, đọc DOCX trong bộ nhớ. Ưu tiên lỗi làm sai authorization, current/proposed/approved state, validation identity, High Risk consent, telemetry freshness hoặc bằng chứng thực thi. Operator vẫn là người sửa thông tin UAV, Manager chỉ duyệt. Không sửa docs đã nộp và không suy diễn Jira Done từ code.
- **Bảo mật và chi phí:** Tìm lỗi mới liên quan IDOR, XSS, lộ key/token, CSRF, RLS/grants, rate-limit bypass, retry/fan-out hoặc provider calls không giới hạn. Kiểm tra ranh giới secrets của PR/fork và môi trường production/preview. Không đọc credential stores, private keys, file `.env`, dump hoặc appsettings máy cá nhân; không in secret.
- **Migration và kết luận review:** Kiểm tra compatibility, locking, data invariants, history và yêu cầu backup/rollback theo mục 5. Chỉ nêu lỗi có căn cứ kèm file/dòng, kịch bản gây lỗi và requirement liên quan; tập trung vấn đề nghiêm trọng thay vì nhận xét hình thức. Phân biệt bug với thiếu evidence. Nếu không truy cập được Jira, backup hoặc deployment hiện hành thì ghi chưa xác minh, không bịa status hay khẳng định đã nghiệm thu. Trả nhận xét bằng tiếng Việt; review không thay thế CI hoặc approval của người có thẩm quyền.

## 1. Tài liệu chuẩn và phạm vi bất biến

- Đọc phần liên quan trong hai nguồn hiện hành trước khi đánh giá hoặc thay đổi nghiệp vụ:
  - `docs/Review 2 submission/Report3_Software Requirement Specification.docx` — SRS.
  - `docs/Review 2 submission/FA26SE238_DRONEOPS_INTELLIGENCE_AN_AI_ASSISTED_MIS_tripm14.docx` — phiếu đăng ký đồ án.
- Dùng `Report4_Software Design Document.docx` trong cùng thư mục để hiểu thiết kế; dùng các tài liệu vận hành trong `docs/` để đối chiếu triển khai. Không lấy tài liệu hướng dẫn hoặc trạng thái Jira làm lý do tự thay đổi phạm vi đã nộp.
- Toàn bộ `docs/` đã nộp hội đồng: **chỉ đọc, không sửa, tạo thêm, đổi tên, xóa, render đè hoặc chạy script cập nhật tài liệu**. Không sửa SRS/SDD/phiếu đăng ký/Report5 để làm code có vẻ phù hợp. Chỉ thay đổi tài liệu khi người dùng trực tiếp cho phép rõ ràng cho file đó.
- Đọc DOCX bằng công cụ chỉ đọc, ví dụ giải nén `word/document.xml` trong bộ nhớ. Bỏ qua file khóa Office `~$*`. Không dùng bản xuất tạm, bản archive hay trí nhớ của lần làm trước làm nguồn chuẩn.
- Nếu tên tài liệu thay đổi, tìm trong đúng thư mục, xác minh nội dung/tên dự án trước khi chọn; không tự dùng một bản cũ có tên gần giống.
- Đối chiếu theo tên nghiệp vụ và UC/FR/BR/NFR; nếu số MF, ownership, sơ đồ hoặc phụ lục trong tài liệu mâu thuẫn thì nêu rõ nguồn/đoạn mâu thuẫn. Không âm thầm chọn cách hiểu thuận với code.
- Khi thiếu hoặc xung đột yêu cầu, tiếp tục phần công việc độc lập; trình bày điểm cần quyết định trong chat. Không tự mở rộng sản phẩm và không đổi nghiệp vụ chỉ để test đạt.
- Quyền sửa thông tin UAV: `uav_operator` gửi đề nghị sửa; Manager duyệt/từ chối. Không cấp quyền sửa thông tin UAV trực tiếp cho Manager. Phân biệt quyền sửa UAV với quyền đề xuất sửa mission theo UC tương ứng.

## 2. Đối chiếu luồng trước và sau thay đổi

1. Kiểm tra `git status`, nhánh/commit, các thay đổi đang có và hướng dẫn cục bộ. Không ghi đè thay đổi của người dùng.
2. Trích yêu cầu áp dụng từ SRS và phiếu đăng ký: actor, precondition, trigger, dữ liệu vào, normal/alternative flow, postcondition, trạng thái, quyền, evidence và NFR liên quan.
3. Lần theo luồng thật: UI → proxy/auth → controller → service → transaction/database → integration/worker → response/UI. Kiểm tra các bước bàn giao giữa mainflows, không chỉ endpoint hoặc component đang mở.
4. So sánh từng yêu cầu với code, schema, test và môi trường đã triển khai. Phân biệt: ngoài phạm vi; thiếu implementation; làm khác đặc tả; có code nhưng chưa kiểm thử; đã kiểm thử nhưng chưa deploy; thiếu bằng chứng nghiệm thu.
5. Sửa nguyên nhân và các contract bị ảnh hưởng; kiểm thử normal flow, lỗi, quyền, stale/concurrent request và retry khi liên quan. Không chỉ sửa happy path.
6. Sau khi sửa, kiểm tra lại diff, luồng nghiệp vụ, bảo mật và kết quả test. Báo rõ việc đã làm và các giới hạn chưa xác minh.

Các ranh giới bắt buộc phải kiểm tra khi có liên quan:

- Current mission, proposed update, review request, validation run và execution snapshot là các đối tượng có ý nghĩa khác nhau. Không ghi đè approved content trước quyết định; reject update phải giữ nội dung/trạng thái hiện hành theo đặc tả.
- Không tự tạo mission-version entity/API. Restriction revision, request identity và immutable evidence vẫn phải giữ.
- Validation phải xét waypoint lẫn segment, đúng đơn vị/altitude reference và restriction revision. Fail/Incomplete/Error/Stale không được biến thành Pass; thiếu phép chuyển đổi không được tự giả định.
- Weather/battery High Risk cần xác nhận đúng nội dung/evidence; xác nhận rủi ro, Manager approval và pilot consent để launch là các quyền khác nhau. AI chỉ tư vấn.
- Cancel trước bay không phải lệnh stop/disarm trên không. ACK/upload/schedule hoặc thao tác web không được tự tạo bằng chứng cất cánh, InProgress hay Completed.
- Telemetry giữ source/receive time, identity, freshness và missing values. Không dùng dữ liệu cũ, replay hoặc giá trị giả để đẩy tiến độ/trạng thái.
- Autopilot giữ stabilization, pre-arm checks và onboard failsafe; không force-arm, bỏ arming checks hoặc thử ARM/START trên drone thật để kiểm tra code.

## 3. Jira: đọc trạng thái mới và đối chiếu ba nguồn

- Với công việc nghiệp vụ/audit/nghiệm thu, truy vấn Jira hiện tại qua connector. Xác minh đúng site và project từ repo/session; project hiện dùng là `KAN`. Không lấy status từ một bản ghi chú cũ làm trạng thái hiện tại.
- Đọc summary, description/acceptance criteria, status, parent, dependencies và các link tài liệu/code/test liên quan. Đọc comments/changelog khi cần giải thích thay đổi hoặc phân biệt yêu cầu mới với ghi chú lịch sử.
- Khi báo task có status mới, so với một baseline có ngày/giờ hoặc lần kiểm tra trước có bằng chứng. Nếu cần, đọc changelog để biết chuyển trạng thái lúc nào. `updated` mới không đồng nghĩa status mới. Không có baseline thì chỉ báo trạng thái hiện tại và nói chưa xác định được thay đổi.
- Kiểm tra task có chỉ đúng tài liệu đang tồn tại trong `docs/`, đúng UC/FR/NFR và mô tả đúng nghiệp vụ không. Link vào đường dẫn đã đổi, file không có, commit cũ hoặc tài liệu khác baseline phải được chỉ ra.
- Đối chiếu cả hai chiều: requirement có task/code/test chưa; task có vượt phạm vi hoặc thiếu acceptance so với tài liệu không. Đặc biệt kiểm tra task Done có code và evidence tương ứng hay chỉ có status.
- Jira Done không chứng minh code đã deploy, migration đã chạy, browser E2E/hardware đã pass. Code/test đạt cũng không tự thay thế nghiệm thu theo tài liệu.
- Trình bày kết quả bằng bảng trong chat: `Requirement/nguồn | Jira key + status + thời điểm | Code/test evidence | Sai khác/còn thiếu | Hành động`.
- Không sửa docs để khớp Jira. Không tự đổi status, description/AC, tạo task hay gửi comment chỉ vì đang đối chiếu; chỉ ghi lên Jira khi phạm vi người dùng đã cho phép bao gồm việc đó. Khi được phép cập nhật, giữ lịch sử, phân biệt bằng chứng mới với ghi chú cũ và không đánh Done khi còn thiếu AC.
- Nếu Jira không truy cập được, nêu rõ giới hạn; vẫn kiểm tra tài liệu/code độc lập, không đoán status hoặc tuyên bố đã đồng bộ.

## 4. Bảo mật là điều kiện hoàn thành

Kiểm tra các mục áp dụng cho thay đổi và những đường gọi liên quan. Trước release hoặc khi được yêu cầu audit bảo mật, mở rộng sang toàn chuỗi Cloudflare → Render → Supabase. Không tuyên bố hệ thống an toàn chỉ từ build/lint hoặc một checklist.

### Auth, dữ liệu và XSS

- Enforce authentication, role và record ownership/managed scope ở backend cho mọi read/mutation/stream/file access; UI guard không thay thế authorization. Kiểm tra IDOR/BOLA và truy cập bằng ID trực tiếp.
- Không tin role, user ID, UAV/mission binding, approval, giá tiền/quota hoặc trạng thái do client gửi. Kiểm tra JWT signature, issuer/audience, expiry và session/revocation theo cơ chế hiện tại.
- Cookie session phải có cấu hình HttpOnly/Secure/SameSite thích hợp; mutation dùng cookie phải có CSRF/origin protection. CORS chỉ mở cho origin cần thiết; không kết hợp credential với wildcard origin.
- Không lưu private/access/refresh token vào HTML, URL/query, browser storage hoặc client bundle nếu có thể dùng session HttpOnly. Không đưa secret vào `NEXT_PUBLIC_*`. Publishable/anon key không phải service-role key; quyền dữ liệu vẫn phải do RLS/authorization giới hạn.
- Kiểm tra untrusted HTML/Markdown, rich text, tên file, provider/AI output, map popup, `dangerouslySetInnerHTML`, `innerHTML` và script/URL injection. Dùng escaping/sanitization phù hợp; allowlist URL scheme, hạn chế script nguồn ngoài và CSP phù hợp. HttpOnly không ngăn XSS thực hiện request thay người dùng.
- Dùng query parameterization; không ghép SQL/shell từ input. Kiểm tra SSRF, redirect, traversal, upload content/type/size và signed URL expiry/scope khi liên quan.
- Với Supabase: kiểm tra RLS và grants thực tế; không mở anon/authenticated rộng để sửa lỗi truy cập. Service-role credential chỉ ở server tin cậy. Kiểm tra view/function có bypass RLS hoặc `SECURITY DEFINER` không cần thiết.

### Private key, token và thông tin nhạy cảm

- Kiểm tra tracked files, diff, cấu hình build/deploy, artifacts/bundle, log/error response và lịch sử Git liên quan để phát hiện credential. Dùng secret scanner nếu có; đừng coi `.gitignore` là bằng chứng secret chưa từng bị commit.
- Không in secret, connection string chứa mật khẩu, private key, raw JWT, OTP hay nội dung private media trong chat/tool output/Jira/test artifact. Chỉ báo đường dẫn, loại secret và dấu hiệu đã che; truyền credential qua cơ chế secret/env phù hợp, tránh command arguments chứa secret.
- Nếu phát hiện lộ credential: ngừng phát tán, xác định phạm vi bằng kiểm tra chỉ đọc, báo ngay và đề xuất rotate/revoke cùng nơi cần cập nhật. Không coi xóa khỏi file hiện tại là đã xử lý credential từng lộ. Không tự rotate credential dùng chung nếu chưa được phép hoặc chưa có kế hoạch cập nhật phụ thuộc.
- Backup/dump chứa dữ liệu và có thể chứa secret: cấm commit hoặc đưa vào thư mục public/artifact công khai. Bảo vệ quyền truy cập, mã hóa và quản lý khóa tách biệt theo hạ tầng hiện có.

### DDoS, abuse và vượt usage/chi phí

- Kiểm tra rate limit theo route, identity và IP phù hợp cho login/OTP/reset/register/upload, telemetry ingestion, AI/weather, report generation và các mutation tốn tài nguyên. Đằng sau proxy, chỉ tin forwarded headers từ proxy đã cấu hình; không để attacker đổi header để né limit.
- Kiểm tra giới hạn body/file, pagination, query complexity, concurrent requests, SSE/WebSocket connections, timeout, cancellation, queue depth, retry/backoff và idempotency. Tránh retry storm, polling khi hidden/offline, stream không cleanup, fan-out hoặc worker không có giới hạn.
- Cloudflare: đối chiếu DNS/proxy route, SSL, WAF/rate-limiting/bot controls và quota hiện có. Kiểm tra khả năng gọi thẳng origin Render để bypass Cloudflare. Không giả định DNS proxy tự bảo vệ origin hoặc tự ngăn mọi DDoS.
- Render: kiểm tra request/connection limits của app, health checks, worker concurrency, resource plan/autoscaling cap, bandwidth/egress và log volume. Kiểm tra endpoint đắt đỏ còn public qua `onrender.com` hay origin khác không.
- Supabase: kiểm tra RLS/grants, connection pool/connection limits, query timeout/indexes, Realtime subscriptions, Storage access/upload/egress, Auth abuse controls và hạn mức gói dịch vụ.
- Kiểm tra cache theo đúng quyền và identity; không cache public dữ liệu riêng. Cache/deduplicate provider calls, đặt budget/timeout và chặn trigger lặp AI/weather/report. Cache không được làm stale safety evidence thành hợp lệ.
- Xác minh cấu hình account/plan/quota/alerts/spend cap qua nguồn hiện hành khi có quyền đọc. Không đoán giới hạn hoặc coi billing alert là hard spending cap; nếu nhà cung cấp không có hard cap, nêu rõ và kiểm tra biện pháp giới hạn ở ứng dụng.
- Không chạy DDoS/load test vào domain, host, provider hoặc Supabase thật. Dùng fixture/local/staging cô lập với tải có giới hạn. Thay đổi firewall, DNS, gói trả phí hay cấu hình production chỉ trong phạm vi đã được người dùng cho phép.

## 5. Migration database: backup trước, đồng bộ Supabase sau

Mỗi thay đổi schema phải có migration lưu trong repo và kiểm tra tương thích code cũ/mới. Trong phạm vi công việc đã được người dùng cho phép thay đổi database, phải đồng bộ migration cần thiết lên đúng Supabase dùng chung để team không bị lệch schema; không chỉ sửa local rồi báo đã xong.

1. Xác minh target từ cấu hình không lộ secret và danh sách project. Project DroneOps hiện có ref `diaswbzjhvzwxcadlias`; luôn kiểm tra lại ref/name/environment trước khi ghi, không áp dụng nhầm project khác.
2. Đọc schema và migration history thực tế; so với `database/migrations/`, `database/migration-order.txt` và schema startup checks. Đừng chỉ dựa vào tên/timestamp: đối chiếu nội dung SQL, vì version được Supabase ghi nhận có thể khác filename local.
3. Preflight: xem column type/nullability/default, constraints/indexes có sẵn, duplicate/null/orphan data, RLS/grants và transaction/locking impact. Kiểm tra migration đang chạy đồng thời, schema drift, pooler/connection mode và quyền DDL. Không tạo index trùng chức năng dưới tên khác.
4. **Trước mọi migration trên database dùng chung, phải có backup mới hoàn tất và xác minh được.** Ưu tiên `scripts/recovery/backup.py` theo hướng dẫn `--help` và cấu hình hiện có; kiểm tra manifest/checksum, thời điểm, đúng database/environment và coverage. Bao gồm private media/storage khi migration hoặc rollback phụ thuộc chúng. Không coi file tồn tại, snapshot chưa hoàn tất hoặc lịch backup được bật là bằng chứng backup dùng được.
5. Có kế hoạch khôi phục tương ứng rủi ro. Dùng `scripts/recovery/restore.py` để rehearsal trên target rỗng, cô lập khi cần; không restore thử đè database dùng chung. Trước migration phá hủy/khó đảo ngược, phải chứng minh restore và có phương án roll-forward/rollback cùng code tương thích. Không có backup hợp lệ thì dừng bước apply, tiếp tục chuẩn bị/kiểm thử độc lập và báo thiếu điều kiện.
6. Thử migration trên PostgreSQL cô lập tương đương: dữ liệu cũ, migration-order, apply lại an toàn hoặc cơ chế chống apply lặp, duplicate/null cases, transaction rollback và các query/luồng bị ảnh hưởng. Kiểm tra retry/concurrency nếu có thay đổi identity, lifecycle, unique constraint hoặc reservation.
7. Ưu tiên migration bổ sung tương thích ngược, transaction khi phù hợp, lock/statement timeout có giới hạn và thao tác ngắn. Đối với concurrent index hoặc thao tác không chạy trong transaction, dùng kế hoạch riêng; không bọc transaction một cách máy móc. Không reset/drop/reseed hoặc tự sửa dữ liệu lịch sử để né lỗi.
8. Khi backup và preflight đạt, áp dụng đúng migration đã review qua cơ chế Supabase có migration history. Không đẩy tất cả migration chưa rõ trạng thái; không chạy DDL thủ công rồi bỏ quên history. Không sửa migration đã áp dụng: tạo migration tiếp theo nếu cần điều chỉnh.
9. Sau apply: đọc lại cột/index/constraints, RLS/grants, history và data invariants; chạy targeted smoke query/backend flow. Không tạo mission, account hay dữ liệu bay giả trong production để test nếu chưa được phép. Kiểm tra lock/deadlock, connection exhaustion, schema-cache và lỗi ứng dụng có liên quan.
10. Nếu timeout/mất kết nối hoặc tool trả kết quả không rõ, đọc schema/history trước khi retry: migration có thể đã commit. Nếu lỗi, xác định transaction đã rollback hay partially applied; tránh retry mù, repair history giả hoặc rollback phá dữ liệu đang phát sinh.
11. Báo team qua kết quả trong chat: project/environment, migration local ↔ remote version/name, backup reference đã che thông tin nhạy cảm, trạng thái apply, verification và bước deploy/restart còn lại. Không sửa docs đã nộp và không gửi tin nhắn bên ngoài nếu chưa được yêu cầu. Nếu không thể apply, báo rõ chưa đồng bộ Supabase; không đánh task migration hoàn tất.

## 6. Kiểm thử và bàn giao

- Chạy kiểm tra đúng phạm vi thay đổi: backend build/tests, FE typecheck/lint/tests, HTTP/DB integration và browser E2E khi luồng UI/API thay đổi. Với tài liệu benchmark/RQ/NFR, dùng đúng profile/dataset và ghi raw measurements; unit tests không thay thế performance/hardware acceptance.
- Chỉ chạy test/load có giới hạn trên môi trường phù hợp. Phân biệt simulator, fixture, PostgreSQL tạm, Supabase dùng chung và drone thật; không ghi Pass cho môi trường chưa chạy.
- Evidence phải nêu commit hoặc workspace chưa commit, môi trường, lệnh kiểm tra và kết quả/giới hạn. Không dùng kết quả test của commit cũ như đã kiểm tra code mới.
- Trước khi bàn giao: `git diff --check`, xác nhận không thay đổi `docs/`, không lộ secret, không ghi đè công việc người dùng và không để service/test job tạm chạy ngoài ý muốn.
- Báo ngắn gọn trong chat: thay đổi nghiệp vụ, kiểm tra đã chạy, Jira/docs/code còn lệch ở đâu, migration/backup/deploy thực sự đã làm, và điều kiện còn thiếu. Không tuyên bố mọi task Done hoặc hệ thống không thể bị tấn công khi thiếu bằng chứng.
