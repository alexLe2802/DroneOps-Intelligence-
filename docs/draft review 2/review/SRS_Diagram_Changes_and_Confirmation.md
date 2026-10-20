# Ghi chú hình cần hoàn thiện sau khi cập nhật SRS v1.3

Tài liệu áp dụng: `Report3_Software Requirement Specification.docx` trong `docs/draft review 2`.

**Không có hình nào đã bị thay thế hoặc chỉnh sửa trong lần cập nhật này.** Toàn bộ media nhúng và XML của drawing (kích thước, vị trí, nội dung) được kiểm tra giữ nguyên. Các việc dưới đây là danh sách để người phụ trách diagram thực hiện sau. Phiếu đăng ký đề tài không được chỉnh sửa.

## 1. Các hình đang sai nội dung hoặc gắn nhầm

| Vị trí trong SRS | Hiện trạng | Thay đổi cần làm sau |
|---|---|---|
| SCR-05 — Areas & Restrictions | Hình đang là Operator roster | Thay bằng restriction editor: polygon, loại vùng, giới hạn độ cao MSL, kiểm tra hình học, lưu restriction revision và báo validation cũ mất hiệu lực. |
| SCR-04 — Operator Management | Dùng hình profile/UAV chung | Dùng operator roster/detail: avatar, email, trạng thái, giấy phép hiện hành, hạn giấy phép, ảnh riêng tư, phân công; có tab đăng ký và yêu cầu đổi giấy phép. |
| SCR-03 — UAV Management | Dùng hình profile/UAV chung | Bổ sung list/detail và form UAV: mã, model, status, telemetry identity, giới hạn tốc độ và lỗi trùng định danh. |
| SCR-08 — Mission Details & Validation | Dùng hình Approval review | Cần trang xem mission cho Operator/Manager: current mission, đề xuất đang chờ, trạng thái, validation map và lỗi theo waypoint/segment, history; chỉ hiện đúng thao tác theo quyền. |
| SCR-13 — Mission History | Dùng hình history/report/AI chung | Cần danh sách chuyến bay, bộ lọc ngày/UAV/status, phân trang và điều hướng chi tiết. |
| SCR-14 — Flight Details & Post-Flight Report | Dùng hình history/report/AI chung | Cần route thực tế, execution snapshot, duration/distance/coverage, incidents, report status và retry/regenerate chỉ dành cho Manager. |
| SCR-15 — AI Assessment | Dùng hình history/report/AI chung | Hiện Top-5 lịch sử tương tự, similarity, đặc điểm, incident evidence và link nguồn; tách phần LLM advice; có no matches, unavailable và outdated. |
| SCR-11 / SCR-12 | Dùng chung hình list/detail | Tách hoặc dùng một trang nhiều panel có nhãn rõ: list với filter/pagination; detail với frozen evidence, timeline, acknowledge và Manager resolve. |
| SCR-02 / SCR-16 | Dùng chung dashboard/analytics | Làm rõ hai màn: overview và analytics có filter, công thức, đơn vị, khoảng dữ liệu và drill-down. |

## 2. Screen Flow — Figure 7

Giữ nguyên hình hiện tại cho đến khi người phụ trách thay. Bản cần sửa sau phải:

- Bỏ dòng nhỏ “screenshots are pending”; không thêm ghi chú biên soạn vào hình nộp.
- Chia các cụm Account, Mission Planning, Monitoring/Incidents, History/AI và Manager Administration; bố trí theo cột hoặc tách thành nhiều trang cùng một file draw.io.
- Mỗi đường phải có source/target gắn vào biên shape; dùng đường thẳng hoặc vuông góc, không đi xuyên shape, không để các đường dùng chung một đoạn khiến không nhận ra điểm đích.
- Giảm giao cắt bằng sắp xếp node, không kéo đường vòng xuyên toàn trang. Nhãn quyền đặt sát đúng đường, có nền trắng và cách nét nối.
- Thêm SCR-17 Registration, SCR-18 Profile, SCR-19 Change Email, SCR-20 My Flight Credentials, SCR-21 License Change Review.
- Operator: mission list -> editor/details -> validation/history; Save update tự gửi review. Manager: approval queue -> review -> decision. Phân biệt initial Submit với Save update.
- Thêm navigation từ roster đến registration/license review; từ profile đến đổi email và gửi giấy phép.
- Có các nhánh từ Live Monitoring/History đến incident detail và AI evidence, không nối các màn chỉ vì thuộc cùng một menu.

## 3. Use Case — Figures 2–6

- Figure 2 Shared Management: thêm Applicant và UC-23 đăng ký/duyệt đăng ký, UC-24 profile/avatar, UC-25 đổi email, UC-26 đổi/duyệt giấy phép. Có thể tách account use cases thành trang riêng trong cùng file draw.io để không chật.
- Actor Applicant đứng ngoài hệ thống; chưa có quyền Operator trước khi duyệt. Chỉ nối Manager đến review, không nối Applicant đến quản trị.
- Figure 3 MF1: cập nhật UC-06 thành đề xuất thay đổi; UC-08 là Submit ban đầu hoặc validation được kích hoạt tự động từ cập nhật. Chỉ vẽ include khi hành vi luôn được gọi; không dùng include để biểu diễn thứ tự màn hình.
- Figure 6 MF4: thêm hành vi retrieve similar historical flights và review historical incident evidence. External AI Provider chỉ tham gia bước tạo lời khuyên, không đóng vai kho dữ liệu lịch sử.
- Mọi association phải chạm biên oval; actor ở hai bên phù hợp, tránh đường giao nhau. Không tự sửa các hình hiện tại trong lần cập nhật nội dung này.

## 4. State Diagram — Figures 14–16

### Figure 14 — Mission lifecycle

- Initial: Draft -> Pending Review -> Approved hoặc Rejected; Rejected có thể submit lại.
- Update là trạng thái riêng của request, không tự hạ Approved về Pending Review và không tạo mission version.
- Guard start: Approved + validation còn hiệu lực + assignment đủ điều kiện + không có pending update.
- In Progress -> Completed chỉ từ sự kiện hoàn tất đáng tin cậy.
- Cancel được từ Draft, Pending Review, Rejected, Approved; không cho executing/terminal.
- Bổ sung chú thích guard ngắn trên cạnh, không thêm đoạn thuyết minh nhỏ dài trong hình.

### Figure 15 — Pending update request lifecycle

- Cho phép Operator hoặc Manager tạo proposal trong phạm vi quyền.
- Các trạng thái: Pending Review, Approved, Rejected, Superseded, Cancelled.
- Save mới supersede request đang pending. Chỉ request hiện hành được quyết định.
- Approve áp dụng payload vào cùng mission ID; Reject không đổi current content/status.
- Author không tự approve; validation Fail/Incomplete/Error/Stale chặn Approve nhưng không chặn Reject.

### Figure 16 — Incident lifecycle

- Giữ Open -> Acknowledged -> Resolved, ghi rõ actor/quyền và yêu cầu note.
- Phân biệt lifecycle xử lý của người dùng với abnormal episode của detector. Resolve không giả lập recovery và không làm phát sinh incident lặp khi metric vẫn bất thường.

## 5. Swimlane — Figures 10–13

**Theo yêu cầu của chủ tài liệu: giữ nguyên swimlane gốc, không tự vẽ lại, chỉnh chữ hoặc xuất lại ảnh.** Những điểm dưới đây chỉ là sai khác cần người sở hữu hình quyết định xử lý sau:

- Figure 10 MF1 còn “Save submitted mission and version”; SRS đã bỏ mission version. Khi chủ hình cho phép chỉnh, đổi sang current mission + pending update, thêm nhánh approve/reject proposal và giữ current plan khi reject.
- Figure 10 hiện chưa thể hiện đầy đủ Save tự gửi review, supersede request, self-approval guard và cấm start khi có pending update.
- Figure 11 AI mới là gọi LLM, thiếu feature extraction/retrieval Top-k và evidence từ lịch sử.
- Figure 12 Monitoring cần đối chiếu timer loss, dedup episode, measured recovery và nguồn sự kiện start/complete.
- Figure 13 Post-flight cần đối chiếu Failed/Retry, late data, Outdated và report revision.
- Nhãn lane/điều kiện chỉ được bố trí lại khi chủ hình cho phép; không thay hình bằng bản tự vẽ trong lần này.

## 6. ERD — Figures 8–9

- Planning: một Mission hiện hành; một pending request hiện hành; lưu proposed payload và decision/evidence identity. Không thêm MissionVersion.
- Flight giữ execution snapshot bất biến; một flight/mission, bay lại tạo mission mới.
- Account: bổ sung/đối chiếu PilotProfile, Registration, current License, LicenseChangeRequest, EmailChallenge và MediaObject, quan hệ owner/reviewer và ảnh riêng tư.
- Operational: bổ sung episode identity và in-app alert/unread state; giữ incident status events và frozen evidence.
- AI: bổ sung feature record/index schema, retrieval run hoặc evidence links, historical flight IDs và similarity; tách retrieval evidence khỏi provider text.
- Report: hỗ trợ report revision, coverage và trạng thái Outdated. Report revision không phải mission version.
- Đây là logical entities; tên bảng vật lý và cách triển khai thuộc SDD.

## 7. Hình mới cần bổ sung

| Hình | Nội dung tối thiểu | Nơi sử dụng |
|---|---|---|
| Registration UI | Form phi công, Briefly describe your flight experience, giấy phép/ảnh, email verification, pending/rejected feedback | UC-23 / SCR-17 |
| Profile UI | Điện thoại, ngày sinh, kinh nghiệm, avatar; loading/success/error rõ | UC-24 / SCR-18 |
| Change Email UI | Email mới, báo trùng, gửi/resend OTP, countdown, xác nhận, lỗi/hết hạn và success | UC-25 / SCR-19 |
| Current / Proposed License UI | Giấy phép hiện hành, upload đề xuất, pending/history/rejection | UC-26 / SCR-20 |
| License Review UI | Avatar, thông tin phi công, ảnh hiện hành/đề xuất, approve/reject reason | UC-26 / SCR-21 |
| Account workflow hoặc sequence | Email challenge và license review, các nhánh thất bại giữ nguyên dữ liệu hiện hành | SDD; SRS có thể dùng activity tổng quan |
| Historical retrieval sequence | Authorized context -> feature representation -> scoped Top-k -> incident evidence -> optional LLM -> retained result | SDD / giải thích MF4 |

Mỗi loại diagram giữ một file draw.io nhiều trang, xuất đủ PNG từng trang; không gom toàn hệ thống lên một trang chật. Không dùng dòng “design illustration only”, “target workflow”, “screenshots pending” hoặc chỉ dẫn người viết bên trong hình nộp.

## 8. Quyết định nhóm cần xác nhận trước khi chốt baseline

Phần chữ đã được viết thành quy tắc kiểm thử được. Các mục dưới đây là **mặc định dự thảo**, không phải khẳng định nhóm đã thống nhất hay code đã triển khai:

1. Mobile: gồm profile, mission/validation viewing, monitoring, alerts, history; route editing và quản trị là Web-only. Cần xác nhận mobile có thuộc deliverable chính thức.
2. AI: giữ yêu cầu retrieval lịch sử/Top-k theo phiếu; LLM là lớp tổng hợp evidence. Cần nhóm/giảng viên xác nhận so với hướng triển khai hiện tại. Không thay đổi phiếu.
3. Không tự approve proposal; pending update chặn start; một pending request; Save mới supersede request cũ; mỗi mission chỉ một flight.
4. WGS84, altitude MSL; chạm biên vùng cấm là violation; thiếu terrain conversion tạo Incomplete. Import restriction và report export ngoài phạm vi bản này.
5. Default detector: loss 10 giây, timer tối đa 1 giây; battery dưới 20%, recovery từ 25%; hai mẫu liên tiếp cho metric/recovery; altitude/speed theo giới hạn đã cấu hình.
6. OTP 6 số, 10 phút, 5 lần thử, resend 60 giây; email mới chỉ có hiệu lực khi xác nhận thành công.
7. Backup hằng ngày, RPO 24 giờ/RTO 4 giờ; operational records 12 tháng; ảnh license cũ/từ chối 90 ngày.
8. Manager retry/regenerate report; late telemetry làm report Outdated; Top-k mặc định 5; AI timeout 30 giây.

Các giá trị này cần đồng bộ sang SDD, test cases và triển khai sau khi nhóm chốt. Tài liệu hiện mô tả yêu cầu; lần chỉnh này không nghiệm thu chức năng phần mềm.

## 9. Trước khi nộp

- Thay hình đúng màn hình theo mục 1; rà toàn bộ phần chữ nằm trong ảnh vì lần này ảnh được giữ nguyên.
- Bổ sung hình account flow và retrieval, cập nhật actor/use-case/ERD/screen flow tương ứng.
- Mở Word cập nhật Table of Contents và số trang, kiểm tra ngắt trang/caption sau khi nội dung dài thêm.
- Đối chiếu lại SDD và Report 5; lần này chỉ hoàn thiện nội dung SRS và file ghi chú này.
