# DroneOps Intelligence

- Dùng tiếng Việt khi báo cáo. Kiểm tra nhánh, diff và thay đổi có sẵn trước khi sửa.
- Nguồn nghiệp vụ: SRS, phiếu đăng ký và SDD trong `docs/Review 2 submission/`; đọc DOCX trong bộ nhớ khi cần. Không sửa tài liệu đã nộp nếu chưa có yêu cầu trực tiếp.
- Operator đề xuất sửa UAV; Manager duyệt. Giữ riêng current/proposed/approved state và evidence. Không coi ACK/upload là bằng chứng bay; không force-arm hoặc bỏ failsafe.
- Kiểm tra authorization/ownership ở backend; giữ telemetry identity, freshness và missing values. Không đọc/in/commit secrets hoặc backup dữ liệu.
- Migration dùng chung cần xác minh target, backup mới đã kiểm tra, preflight, thử cô lập và verification; không tự deploy hay ghi production ngoài phạm vi được yêu cầu.
- Jira Done không chứng minh đã deploy/nghiệm thu. Chỉ cập nhật Jira hoặc gửi tin bên ngoài khi được yêu cầu.
- Chạy kiểm tra phù hợp với diff và `git diff --check`; báo đúng kết quả và giới hạn.
- Frontend Next.js: đọc guide tương ứng trong `fe/node_modules/next/dist/docs/` trước khi sửa API/framework.

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
