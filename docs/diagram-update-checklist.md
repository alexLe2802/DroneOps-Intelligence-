# Diagram cần cập nhật

Các hình và diagram hiện có được giữ nguyên. Danh sách này chỉ dẫn phần cần sửa bằng công cụ thiết kế gốc; chưa có hình nào được cập nhật tự động.

Phân công giữ nguyên số mainflow: MF1 NghiaHQ; **MF2 ThongLQ — realtime, incidents và flight preparation**; MF3 DucDDA; **MF4 KhanhGNG — AI**. ThongLQ giữ vai trò trưởng nhóm và điều phối hồ sơ.

## Report 1

| Vị trí | Nội dung cần bổ sung |
| --- | --- |
| Figure 6.1a — FE-01 / FE-02 | Lựa chọn AutomaticFlightRequested trong kế hoạch; chỉnh lựa chọn phải qua đề xuất/duyệt. |
| Figure 6.1b — FE-03 / FE-04 | Duyệt đúng plan hash, preflight mới trước xác nhận readiness; tách approval với consent của pilot. |
| Figure 6.1c — FE-05 / FE-06 | Gateway enrollment, mission lease/connection, UID flight controller; presence, telemetry freshness và prompt readiness. |

## SRS và bản SRS revised ở thư mục gốc

| Diagram | Nội dung cần bổ sung |
| --- | --- |
| Figure 1 — System Context | Browser/operator, API và trusted gateway; phân biệt telemetry, readiness và command execution theo hợp đồng thực thi. |
| Figure 3 — MF1 use cases | Opt-in trong kế hoạch và review thay đổi lựa chọn tự động bay. |
| Figure 4 — MF2 use cases | Xem readiness, gửi presence, confirm/cancel, xem lý do chặn; quyền theo chủ mission. |
| Figure 7 — Web Screen Flow | Mission option → approval → prompt T−5 → confirm/cancel → readiness; luồng hết cửa sổ, đổi connection và mất mạng. |
| Figure 8 — ERD | Mission.AutomaticFlightRequested, MissionAutomationState, DroneConnection và trusted gateway enrollment; liên kết UAV/UID, plan hash và connection. Đối chiếu tên bảng vật lý theo migration hiện hành. |
| Figure 9 — Planning activity | Opt-in đi qua proposal/approval; thay kế hoạch không tái sử dụng xác nhận cũ. |
| Figure 11 — Realtime activity | Gateway/telemetry freshness, presence và preflight → readiness; nhánh blocked/cancel/expired. |
| Figure 13 — Mission Lifecycle | Tách readiness khỏi mission lifecycle. Confirm không tạo flight; chỉ chuyển InProgress sau bằng chứng bay thực. Thêm dispatch và observed-state reconciliation. |
| SCR-07 / SCR-08 screen mockups | Automatic-flight option, upload/read-back, consent, lý do chặn, dispatch và trạng thái thực thi. Đây cũng là hình nên chưa được sửa. |

## SDD

| Diagram | Nội dung cần bổ sung |
| --- | --- |
| Figure 1 — System Architecture; Figure 2 — Deployment and Trust Boundaries | Python gateway trên Jetson/laptop; HTTPS/WSS outbound, trusted credential và mission lease; scheduler, compiler/transfer, execution adapter và portal. |
| Figure 3 — Backend Package; Figure 4 — Web Package | MissionAutomationController/Service, preparation state/schema và FlightPreparationPrompt. |
| Figure 5 — ERD | Các entity/thuộc tính enrollment và readiness nêu ở ERD SRS; quan hệ/hash/connection và dữ liệu hết hiệu lực. |
| Figure 6 — Planning Class; Figures 7–8 — Submit/Approve Update | AutomaticFlightRequested thuộc nội dung được duyệt, tham gia hash và không tự bật cho mission đã duyệt. |
| Figure 10 — Mission State; Figure 12 — Planning Workflow | Phân biệt opt-in, approval, readiness và thực thi; không thêm transition bay từ thao tác confirm. |
| Figure 13 — Telemetry Class; Figure 14 — Ingestion Sequence | Enrollment/lease, kiểm tra UID và source freshness. Readiness tiêu thụ telemetry thực của đúng connection. |
| Cần thêm sequence thực thi riêng | due → presence → confirm/preflight → kiểm tra gate tại scheduled start → envelope → local pre-arm → autopilot ACK → observed flight. Có nhánh upload/read-back, cancel, stale, expired và replay/restart. |
| Cần thêm state readiness riêng | Thể hiện điều kiện mở cửa sổ, blocked, confirmed và invalidated/cancelled; đây là trạng thái suy ra, không tự khẳng định đã có enum/state machine lưu trong database. |

## Slide Review 2

Đối chiếu các hình đang có ở slide **4** (context), **5** (planning activity), **6** (AI activity), **7** (realtime activity), **8** (post-flight activity), **9** (ERD), **10** (architecture), **11** (packages), **12** (mission/update state). Ưu tiên sửa 4–5, 7, 9–12 theo SRS/SDD; slide 6 và 8 chỉ cần sửa nếu có nhãn chủ flow hoặc giả định thực thi bị ảnh hưởng.

Hai slide chữ trước trang cảm ơn trình bày ownership và quy trình thực thi tự động. Hình gốc được giữ nguyên.

## Quy tắc dùng chung khi sửa

- Cửa sổ xác nhận: **T−5 phút ≤ backend now < start**. Presence gửi 10 giây/lần, hiệu lực 30 giây; gateway heartbeat trong 30 giây, telemetry trong 15 giây, preflight Pass đúng hash trong phút gần nhất.
- Confirmation gắn với plan hash, connection và UAV/UID đúng phạm vi. Hidden/offline tab, cancel hoặc quá giờ không đồng nghĩa consent.
- `dispatchEnabled` phản ánh cấu hình release và toàn bộ gate. Biểu diễn đầy đủ upload/read-back, scheduler dispatch, envelope, local pre-arm, ACK và observed state. Deadline là scheduled start + 5 giây; restart/reconnect không tạo thêm launch.
