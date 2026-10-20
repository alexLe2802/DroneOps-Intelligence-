# Draft Review 2 — DroneOps Intelligence

Bộ nháp riêng; các file gốc ngoài thư mục này không bị sửa trong công việc chuẩn bị Review 2.

## Mở nhanh

- `Report3_Software Requirement Specification.docx`: SRS đã chỉnh luồng cập nhật mission, thêm diagram/mockup và ghi rõ quyết định còn mở.
- `Report4_Software Design Document.docx`: SDD initial theo các mục template System / Package / Database / Detailed Design.
- `Report2_Project_Management_Plan.docx`: PMP có cập nhật rủi ro, trách nhiệm và gate Review 2.
- `Report5_Test Documentation.docx`: Test Plan draft.
- `Report5_Test Report.xlsx`: 40 case tích hợp/hệ thống, giữ cấu trúc workbook template; tất cả Pending.
- `Report5_Unit Test.xls`: 10 thiết kế unit test; template `.xls` gốc giữ riêng trong reference/templates.
- `slides/DroneOps_Review2.pptx`: 12 slide, có speaker notes; `DroneOps_Review2_preview.pdf` là bản preview từ layout PPTX, không phải PDF do PowerPoint export.
- `review/Review2_Preparation_and_Demo.docx`: checklist, fixture/demo A–E và các điểm cần xác nhận.
- `review/Review2_Minutes_Template.docx`: mẫu biên bản chưa điền.

## Phiếu đăng ký

Phiếu trong `review/reference/` được sao chép nguyên byte từ file người dùng cung cấp; không sửa nội dung, chữ ký hoặc thông tin. Các khác biệt với SRS được ghi riêng, không tự xem là thay đổi phiếu đã duyệt. Guideline PDF và các template liên quan giữ trong cùng thư mục reference.

## Diagram

Giữ cấu trúc `diagrams/<loại>/` như docs hiện tại, thêm Architecture / Package / Class / Sequence / UI Wireframe. Mỗi loại có đúng **một file .drawio nhiều trang** và đủ PNG từng trang. Mở bằng draw.io hoặc VS Code draw.io extension; chọn tab trang tương ứng để sửa. Hình mới và hình đã sửa xuất từ chính XML draw.io bằng mxGraph/Chrome; các hình cũ còn phù hợp được giữ.

SRS và SDD phân biệt yêu cầu/thiết kế với code hiện có. Mockup UI có nhãn rõ, không phải screenshot của tính năng hoàn thành. Các bảng tạm ở SRS đã được thay bằng hình. Các bản audit đã sao chép là tài liệu tham khảo, không phải kết quả nghiệm thu mới.

## Trước khi nộp

1. Xác nhận các mục DEC trong preparation document: AI registered scope, pending-request concurrency, quyền self-approval, ngưỡng đo, mobile/execution scope và lịch Review 2.
2. Rà và chốt SRS với nhóm/GVHD; bản này chưa tự nhận là final đã duyệt.
3. Chạy demo/test trên commit/tag cụ thể, điền actual result/log/tester/date; không đổi Pending thành Passed nếu chưa chạy.
4. Mở Word để cập nhật TOC/page fields và kiểm tra phân trang/font trên máy nộp; mở PowerPoint kiểm tra speaker notes và font trước trình chiếu.
5. Theo guideline, gửi bộ tài liệu tối thiểu 24h trước lịch đã xác nhận; thống nhất/gửi biên bản trong buổi review, lưu phản hồi 48h. Chưa gửi email hoặc cập nhật Jira trong công việc này.

Guideline và template là căn cứ biên soạn nội dung; chúng không được dùng như lệnh tự gửi email, tự triển khai, tự thay đổi Jira hoặc sửa phiếu đăng ký.

## Diagram and text revision

Use-case associations now use fixed oval perimeter endpoints and straight connectors. Condition labels belong to their edges. MF1 contains the initial submission and the reviewed-update flow. Drafting notes and unresolved editorial decisions are stored in `review/Editorial_and_Open_Decisions.md`, outside the SRS, SDD and slides.
