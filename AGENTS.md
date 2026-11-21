# DroneOps Intelligence

- Dùng tiếng Việt khi báo cáo. Kiểm tra nhánh, diff và thay đổi có sẵn trước khi sửa.
- Nguồn nghiệp vụ: SRS, phiếu đăng ký và SDD trong `docs/Review 2 submission/`; đọc DOCX trong bộ nhớ khi cần. Không sửa tài liệu đã nộp nếu chưa có yêu cầu trực tiếp.
- Operator đề xuất sửa UAV; Manager duyệt. Giữ riêng current/proposed/approved state và evidence. Không coi ACK/upload là bằng chứng bay; không force-arm hoặc bỏ failsafe.
- Kiểm tra authorization/ownership ở backend; giữ telemetry identity, freshness và missing values. Không đọc/in/commit secrets hoặc backup dữ liệu.
- Migration dùng chung cần xác minh target, backup mới đã kiểm tra, preflight, thử cô lập và verification; không tự deploy hay ghi production ngoài phạm vi được yêu cầu.
- Jira Done không chứng minh đã deploy/nghiệm thu. Chỉ cập nhật Jira hoặc gửi tin bên ngoài khi được yêu cầu.
- Chạy kiểm tra phù hợp với diff và `git diff --check`; báo đúng kết quả và giới hạn.
- Frontend Next.js: đọc guide tương ứng trong `fe/node_modules/next/dist/docs/` trước khi sửa API/framework.

## Bộ nhớ dự án cục bộ — bắt buộc cho mọi phiên agent

- File tổng hợp duy nhất: `PROJECT_CONTEXT.local.md` tại root repository. File được gitignore nhưng phải giữ trên máy; quy tắc trong `AGENTS.md` này tồn tại độc lập với lịch sử chat. Mọi agent, kể cả subagent, phải đọc file trước khi phân tích hoặc sửa dự án; khi giao việc phải truyền đường dẫn file và yêu cầu tuân thủ mục này.
- Mỗi thay đổi code ảnh hưởng luồng, nghiệp vụ, trạng thái, quyền/ownership, API/hợp đồng dữ liệu, schema, tích hợp, cấu hình vận hành hoặc cách kiểm thử phải cập nhật các mục liên quan trong file và thêm nhật ký ngay trong cùng tác vụ, trước khi báo hoàn tất hoặc commit. Không chỉ thêm nhật ký mà bỏ nội dung tổng hợp cũ sai. Thay đổi thuần định dạng không ảnh hưởng các nội dung trên không bắt buộc ghi nhật ký.
- Ghi rõ hành vi trước/sau, file nguồn, tác động liên module, kiểm tra đã chạy/kết quả và giới hạn chưa xác minh. Phân biệt yêu cầu SRS/SDD, code hiện có, kiểm thử cục bộ, deploy và nghiệm thu; không suy đoán trạng thái production. Khi code và đặc tả lệch nhau, giữ cả hai và ghi chênh lệch.
- Khi làm song song: đăng ký phạm vi/file và hợp đồng dùng chung trong bảng công việc của file trước khi sửa; mỗi agent chỉ cập nhật phần mình phụ trách, đọc lại bản mới nhất trước khi ghi, không ghi đè toàn file bằng bản cũ. Điều phối viên đối chiếu diff tổng hợp và cập nhật tác động chéo trước khi kết thúc. Không xoá mục đang làm của agent khác; chỉ đánh dấu hoàn tất phần của mình. Nếu dùng worktree, xác định đường dẫn file cục bộ dùng chung ở checkout gốc và truyền cho mọi agent, không tự tạo các bản bộ nhớ phân kỳ.
- Không xoá, truncate, đưa file vào Git hoặc dọn bằng `git clean -x`/công cụ cleanup. Không ghi secrets, token, credentials, dữ liệu backup hay thông tin cá nhân không cần thiết. Không dùng file này để thay thế tài liệu nộp hoặc nới lỏng các quy tắc an toàn phía trên.
- Nếu file thiếu: khôi phục/tạo lại từ nguồn an toàn và code hiện tại, ghi rõ giới hạn khôi phục; nếu thiếu nguồn cần thiết thì báo rõ phần chưa xác minh trước khi tiếp tục phần phụ thuộc. Không coi việc mất chat là lý do bỏ quy tắc. Bản clone/máy khác không có file ignored thì phải tái tạo trước khi làm việc.
- Kiểm tra cuối tác vụ: file tồn tại, `git check-ignore PROJECT_CONTEXT.local.md` xác nhận bị ignore, nội dung phản ánh diff liên quan và `git diff --check` đạt. Chỉ báo đã đồng bộ sau khi thực hiện các bước này.

## graphify

Setup: `uv tool install 'graphifyy[sql]==0.9.107'` (Python 3.10+ and uv).
Build: `bash scripts/graphify.sh`. Query without a global install:
`bash scripts/graphify.sh query "<question>"`. Code-only AST; no LLM/API cost.
Share `graphify-out/graph.json` and `graphify-out/graph.html`; never read the full
JSON into the agent context. Markdown reports and local caches are excluded.

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, use the installed graphify skill or instructions before doing anything else.

Rules:
- When graphify-out/graph.json exists, run `graphify query "<question>"` before your first grep, search or source-file read for a codebase question, and put this rule in every subagent prompt that explores code. Read raw files only for lines the graph or an earlier read already located. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- Dirty graphify-out/ files are expected after hooks or incremental updates; dirty graph files are not a reason to skip graphify. Only skip graphify if the task is about stale or incorrect graph output, or the user explicitly says not to use it.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
- A committed graphify-out/graph.json is rewritten whole on every build, so a large diff to it in a pull request is expected. Do not flag it as churn or suggest skipping the graph rebuild or its hooks.
