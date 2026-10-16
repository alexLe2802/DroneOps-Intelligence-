# Đối chiếu API — SRS — Jira, 16/10/2026

## Phạm vi và căn cứ

- SRS: `docs/Report3_Software Requirement Specification.docx`, v1.0, 15/09/2026; UC-01–22, FR-01–22, authorization 3.1.3, BR/CR và các quyết định mở D-01–10.
- Jira: project KAN tại https://fpt-team-sxbh4ph6.atlassian.net; đọc đầy đủ 37 issue trả về, bao gồm description và trạng thái. Các task report/slide/setup không được nghiệm thu chỉ từ API.
- Checkout hiện tại HEAD `96ac309`; nhánh backend mới nhất `origin/be-develop` tại `3089344` được fetch để kiểm tra, không merge/checkout hoặc sửa implementation.
- Swagger đang chạy: `http://localhost:5159/swagger/v1/swagger.json`, 46 path / 53 operation. Source backend trong checkout cũ hơn Swagger; kết luận API mới dựa trên Swagger và source nhánh backend, không chỉ source local.
- Kiểm tra HTTP anonymous: `/api/admin/uavs`, `/api/users`, `/api/missions`, `/api/missions/my`, `/api/missions/pending-approvals`, `/api/live/missions`, `/api/incidents` trả 401. `/api/ai/assessments` và `/api/alerts` trả 404. 401 chỉ xác nhận chặn anonymous, không chứng minh đúng nghiệp vụ/scope với user đăng nhập.
- Không gọi mutation backend, không tạo dữ liệu thử, không thử provider/SMTP/UAV thật. Không có tài khoản/session kiểm thử để xác nhận happy path hoặc lỗi profile đã báo trước đó.
- `npm run typecheck` thất bại: generated route `mission-approvals` cũ, `UavForm aircraft` không khớp props, thiếu import `ApiError`, mock `batteryPercent` không thuộc `AssignedUav`. Không sửa các lỗi này trong đợt audit.
- Không đổi bất kỳ issue đang Done về trạng thái khác. Không coi endpoint tồn tại là đủ để Done. Không tự thêm yêu cầu về token CSRF đã được người dùng yêu cầu bỏ trong phiên làm việc này.

## Trạng thái được cập nhật

Các chuyển trạng thái dự kiến dưới đây đều từ To Do sang In Progress; kết quả thực tế được ghi ở cuối báo cáo sau khi Jira xác nhận.

| Issue | Bằng chứng đã bắt đầu | Phần còn thiếu để hoàn thành |
|---|---|---|
| KAN-30 | Epic MF02 đã có ingest/latest/live missions, SSE, incident list/detail và detector một phần | Gateway MAVLink, detector đầy đủ/timer, alert/actions và FE realtime chưa hoàn tất |
| KAN-32 | Epic MF01 có Draft create, geofence CRUD, list/detail/update và approval API | Version lifecycle, submit/validation/cancel và FE toàn luồng còn thiếu |
| KAN-33 | Swagger có GET missions, GET missions/my, GET/PUT missions/{id}; source remote đã triển khai service/repository | List chưa có search/status/date/pagination; detail chỉ Manager; update xóa/thay waypoint trực tiếp, chưa version/state guard/expectedVersion/409 |
| KAN-37 | Source remote TelemetryIngestionService có detector LowBattery và GeofenceBreach, dedup incident chưa Resolved, evidence | Chưa có acknowledge/resolve/alerts API; chưa thấy timer communication-loss khi không packet, rule altitude/speed, threshold cấu hình hoặc append-only status history |
| KAN-38 | FE có registry/list/detail, route map/create và trạng thái loading/error; tái dùng KAN-26/27 là công việc đã triển khai trong scope task | Chưa tích hợp full lifecycle/version/validation/approval/cancel; getPilotMissions vẫn lấy flight-history, latestVersion=1; không có approval review route thực |

## Issue Done có khoảng trống — giữ nguyên Done

| Issue | Kết quả đối chiếu | Bằng chứng / SRS |
|---|---|---|
| KAN-14 — BE Login | Chưa đủ UC-01: login không kiểm tra active account trước phát JWT; chưa thấy JWT token validation kiểm tra IsActive/revocation. Backend không có logout/revoke endpoint. Phần đăng nhập có BCrypt/JWT, không kết luận login hoàn toàn chưa làm. | AuthService.LoginAsync: verify password → RequiresFirstLogin → GenerateToken; Program.cs chỉ JWT signature/issuer/audience/expiry. FR-01.1/.2 yêu cầu active account và sign-out invalidates session. Cần test inactive và JWT đã cấp. |
| KAN-23 — FE Trang pilot | Có trang profile/UAV/mission nhưng chưa đủ phạm vi quản lý nhiệm vụ end-to-end; chưa tích hợp live/incidents. | portal-navigation.ts, lib/portal/client.ts, missions/[id]/mission-detail-client.tsx. UC-05–08/13–16; task mô tả rộng nên đây là khoảng trống chức năng, không phủ nhận các trang đã có. |
| KAN-25 — FE UAV registry | List/create/detail có; yêu cầu task còn xóa và update chờ admin. Không có PUT/DELETE UAV trong Swagger; edit UI truyền aircraft vào UavForm chỉ hỗ trợ create, gây TypeScript error. | uav/[id]/edit/uav-edit-client.tsx:25, uav/uav-form.tsx; UC-02/FR-02. SRS cần chốt policy Operator create/edit vì baseline mô tả Operator view-only. |
| KAN-29 — BE gateway/latest | Có normalized JSON ingest, active missions và latest projection; source remote đã bổ sung validation/out-of-order/freshness/progress. Chưa thấy raw MAVLink decoder/UDP/TCP receiver/message mapping trong repo; không có bằng chứng đường Mission Planner forwarding → decoder → persist. SourceId trên payload chưa có mapping cấu hình nguồn-UAV được thấy trong service. | TelemetryIntegrationController, TelemetryIngestionService, IntegrationAuthorizeAttribute; UC-13, task yêu cầu raw forwarding. Nếu gateway nằm ngoài repo, cần cung cấp source/deployment và integration evidence trước kết luận hoàn thành. |
| KAN-35 — BE approvals | Ba endpoint đã có, có reason/self-approval/audit và kiểm tra sơ bộ. Chưa đủ mandatory validation: CurrentPlanVersion=1 cố định; validationRunId được nhận/trả nhưng không tra validation run/restriction revision; EvaluatePlanIssues chỉ xét UAV/waypoint count/schedule. Chưa có cơ chế version concurrency thật. | MissionApprovalService trên origin/be-develop; FR-09, FR-10, FR-11; KAN-34 chưa cung cấp engine/run/history. Transaction đơn thuần không đảm bảo hai quyết định đồng thời không xung đột. |
| KAN-36 — BE stream/incidents | Có SSE telemetry/incidentRaised/incidentUpdated, keepalive và reconnect snapshot; có incident list/detail/evidence. Còn khoảng trống: chỉ gửi telemetry khi SourceTimestamp đổi nên thay đổi freshness/offline không có event khi ngừng packet; event id reset mỗi kết nối; scope cho non-manager chỉ lọc khi currentUserId.HasValue, chưa fail-closed khi thiếu claim id. Managed scope chưa có filter cụ thể ngoài isManager. Chưa có integration evidence. | LiveStreamController, IncidentService remote; UC-14–16, CR-01. Cần kiểm thử stale/offline, reconnect/resync, thiếu identifier và khác scope; không kết luận toàn bộ stream chưa làm. |

KAN-15/16/18/20 đã có registration/login/OTP/form tương ứng; chưa có đủ thử nghiệm SMTP/activation để nghiệm thu lại. Không tự thay đổi Done. Public registration hiện có khác giả định SRS “no public self-registration”; coi là quyết định D-01 cần chốt, không tự kết luận toàn bộ registration sai.

## Task còn lại

| Issue | Trạng thái giữ nguyên | Kết quả |
|---|---|---|
| KAN-24 | In Review | GET/PUT profile, GET pilot-profile có; avatar, xác minh đổi email và license-change approval chưa có API; UI hiện sửa fullName. Chưa đủ toàn bộ acceptance nhưng đã có phần để review. |
| KAN-26 | In Review | Draft create/route map có; cần nghiệm thu user thật, assigned active UAV và unsaved navigation. Không nâng Done từ source alone. |
| KAN-27 | In Review | CRUD geofence và polygon map có theo task; SRS mở rộng còn restriction type/altitude/reference/revision/stale validation thuộc KAN-34. Không coi basic polygon CRUD là hoàn tất UC-04 toàn bộ. |
| KAN-28 | In Progress | Operators và full UAV list đã nối; list API dùng /users thay /accounts. Provision/access/assignments/license revoke thiếu; approval/live/incidents FE chưa hoàn tất. |
| KAN-31 | To Do | Không thấy weather GET/refresh endpoint hoặc waypoint forecast adapter trong backend. |
| KAN-34 | To Do | Swagger chưa có versions/submit/validation/cancel; source remote chưa có các endpoint/engine này. |
| KAN-39 | To Do | Navigation live/incidents có nhãn nhưng chưa có page/client thực, EventSource hoặc API integration; không xem historical chart/playback là live. |
| KAN-40 | To Do | /users list đã có nhưng 3 contract /accounts, POST provision, PATCH access chưa có trong Swagger/controller. Không đổi task này thành Done chỉ vì roster đã tải qua /users. |
| KAN-1/2/3/5/6/7/8/9/10/11/12/13/17/19 | Giữ nguyên | Report/review/setup/landing nằm ngoài bằng chứng API; không suy diễn hoàn thành hoặc chưa hoàn thành từ API audit. |

## Đối chiếu SRS theo use case

| SRS | API / implementation hiện thấy | Đánh giá |
|---|---|---|
| UC-01 | login/register/OTP/reset password; JWT/BFF cookie | Một phần; active/revocation/logout server và lỗi profile cần xác minh |
| UC-02 | uavs/myUAV, POST uavs, admin list/pending/detail/activate/reject/deactivate | Một phần; chưa update, telemetry identity/audit/concurrency/active-flight guard chưa đủ bằng chứng |
| UC-03 | users list/profile, pilot-profile, pilot registrations approve/reject | Một phần; access enable/disable, manager update/assignment và license revoke chưa có contract |
| UC-04 | geofences CRUD | Có basic polygon CRUD; thiếu restriction revision/type/altitude/reference |
| UC-05 | POST missions, map waypoints | Có Draft create; cần kiểm tra active UAV và error scenarios |
| UC-06 | PUT missions/{id} | Có update trực tiếp; thiếu retained version/state constraints/conflict |
| UC-07 | GET missions, missions/my, detail | Một phần; detail Operator bị chặn bởi action Manager-only; chưa cancel |
| UC-08 | Không có submit endpoint | Chưa thấy triển khai submission + validation gate |
| UC-09 | pending-approvals, approve/reject | Có API nhưng chưa authoritative route-validation run/version gate |
| UC-10 | MissionApproval ghi decision | Có lưu decision; thiếu API/history theo version |
| UC-11/12 | Basic polygon/geofence code | Chưa có API validation run/evidence waypoint+segment và stale revision |
| UC-13 | JSON samples, latest/live list | Một phần; raw MAVLink gateway chưa thấy và chưa test source mapping end-to-end |
| UC-14 | SSE/latest có ở BE | Một phần; FE chưa nối, stale-without-new-sample chưa được event hóa |
| UC-15 | LowBattery/GeofenceBreach detector trong remote | Một phần; chưa timer comm-loss, altitude/speed rules và persistent user alerts |
| UC-16 | incidents list/detail/evidence | Một phần; thiếu acknowledge/resolve/actions/history workflow |
| UC-17 | POST flight-history/{missionId}/finalize, GET report | Có finalize/report service; chưa có bằng chứng completion source signal và report Failed/Retry lifecycle; finalize hiện cập nhật mission Completed trực tiếp |
| UC-18 | history/list, telemetry, incidents, report | Có API lịch sử và FE; cần nghiệm thu scope, incomplete-data/report state và bộ lọc theo SRS |
| UC-19/20 | FE gọi /ai/assessments; backend có provider service | API /ai/assessments trả 404, không xuất hiện trong Swagger; provider class không đủ làm exposed feature |
| UC-21/22 | flight-history/analytics, dashboard FE | Một phần: chủ yếu dữ liệu lịch sử, chưa đủ live aggregation/freshness/filters/formulas và scope evidence |

Các D-01–10 còn mở trong SRS là giới hạn của baseline; không coi ngưỡng/rule đề xuất là quy định đã chốt. Report này không thay thế nghiệm thu tích hợp, kiểm thử quyền có đăng nhập hoặc xác nhận deployment.

## Kết quả cập nhật Jira

Jira đã xác nhận thành công cả 5 transition (id 21):

- KAN-30: To Do → In Progress.
- KAN-32: To Do → In Progress.
- KAN-33: To Do → In Progress.
- KAN-37: To Do → In Progress.
- KAN-38: To Do → In Progress.

Không chuyển task nào sang Done; không đổi status của bất kỳ task Done. Không chỉnh description, assignee, sprint hoặc đăng comment/thông báo.

## Điểm cần ưu tiên kiểm thử

1. MissionApprovalService: version/run/restriction validation thật trước approve; hiện có thể trả Passed từ kiểm tra metadata đơn giản.
2. MissionService.UpdateAsync: state guard, giữ version và optimistic concurrency trước xóa/thay waypoint; Operator phải đọc được detail của own/assigned mission.
3. PostFlightController: finalize/report/telemetry/incidents chỉ có controller-level Authorize; các action detail/finalize không truyền current user/scope vào service. Cần kiểm thử Operator truy cập hoặc finalize mission khác. List filter không bảo vệ các action detail riêng này.
4. AuthService/Program.cs: inactive accounts và JWT đã cấp sau disable/logout; sessionFromToken không chứng minh backend có revocation.
5. LiveStreamController/IncidentService: thiếu user identifier phải deny; freshness/offline event khi không có packet; managed scope và reconnect kiểm thử thật.

Các kiểm thử quyền hoặc mutation trên dữ liệu thật chưa được thực hiện trong audit này.

## API inventory từ Swagger đang chạy


| Method | Route |
|---|---|
| GET | `/api/admin/uavs` |
| GET | `/api/admin/uavs/pending` |
| GET | `/api/admin/uavs/{id}` |
| PUT | `/api/admin/uavs/{id}/reject` |
| PUT | `/api/admin/uavs/{id}/activate` |
| PUT | `/api/admin/uavs/{id}/deactivate` |
| POST | `/api/auth/login` |
| POST | `/api/auth/register` |
| POST | `/api/auth/verify-register` |
| POST | `/api/auth/forgot-password` |
| POST | `/api/auth/reset-password` |
| GET | `/api/geofences` |
| POST | `/api/geofences` |
| GET | `/api/geofences/{id}` |
| PUT | `/api/geofences/{id}` |
| DELETE | `/api/geofences/{id}` |
| GET | `/api/incidents` |
| GET | `/api/incidents/{id}` |
| GET | `/api/live/missions` |
| GET | `/api/live/stream` |
| GET | `/api/missions/pending-approvals` |
| POST | `/api/missions/{id}/approve` |
| POST | `/api/missions/{id}/reject` |
| GET | `/api/missions` |
| POST | `/api/missions` |
| GET | `/api/missions/my` |
| GET | `/api/missions/{id}` |
| PUT | `/api/missions/{id}` |
| GET | `/api/missions/{id}/telemetry/latest` |
| POST | `/api/mission-suggestions` |
| GET | `/api/mission-suggestions/mine` |
| GET | `/api/mission-suggestions/{id}` |
| POST | `/api/pilot-registrations` |
| GET | `/api/pilot-registrations` |
| GET | `/api/pilot-registrations/{id}` |
| POST | `/api/pilot-registrations/{id}/approve` |
| POST | `/api/pilot-registrations/{id}/reject` |
| POST | `/api/pilot-registrations/first-time-login/resend-otp` |
| POST | `/api/pilot-registrations/first-time-login` |
| GET | `/api/flight-history` |
| POST | `/api/flight-history/{missionId}/finalize` |
| GET | `/api/flight-history/{missionId}/report` |
| GET | `/api/flight-history/{missionId}/telemetry` |
| GET | `/api/flight-history/{missionId}/incidents` |
| GET | `/api/flight-history/analytics` |
| POST | `/api/integrations/telemetry/samples` |
| GET | `/api/test` |
| POST | `/api/uavs` |
| GET | `/api/uavs/myUAV` |
| GET | `/api/users` |
| GET | `/api/users/profile` |
| PUT | `/api/users/profile` |
| GET | `/api/users/pilot-profile` |
