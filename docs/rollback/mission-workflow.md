# Hoàn tác phần mission mới sau khi merge main

Merge `4177273` giữ main ở `501cffa`. Tính năng mission nằm trong `fb74f98`,
giao diện và xử lý thiếu dữ liệu nằm trong `cb8a999`.

Không revert merge commit: thao tác đó sẽ gỡ cả phần main vừa nhập.
Revert riêng fb74f98 có thể conflict với main và còn để lại UI phụ thuộc.

Bản vá kèm theo đã kiểm tra áp dụng trên 4177273. Nó đưa code về main 501cffa,
đồng thời giữ bản sửa transaction MissionApprovalService từ bb33f6f.
Live Monitoring, luồng giấy phép hợp nhất và các file khác của main vẫn được giữ.
Bản vá chưa được áp dụng trên Qthong.

Khi quyết định bỏ cả tính năng mission và UI mới, từ working tree sạch:

```sh
git apply --check docs/rollback/mission-workflow.patch
git apply docs/rollback/mission-workflow.patch
git add -A
git commit -m "revert(missions): bỏ luồng mission mới, giữ các chức năng main"
```

Sau đó build backend, chạy typecheck/test/lint frontend và kiểm tra luồng thực tế
trước khi push/deploy. Nếu --check báo lỗi, dừng để giải quyết theo các thay đổi mới;
không ép áp dụng patch. Bản vá là snapshot, không bảo đảm áp dụng sau các commit sau này.

Bản vá chỉ hoàn tác code. Nó không xóa bảng/cột, không hoàn tác migration hay các
mission đã tạo/chỉnh sửa/duyệt trong DB. Các proposal chưa xử lý sẽ không còn giao
diện xử lý sau rollback; cần thống kê và xử lý trước khi triển khai rollback.
Không tự chạy DROP hoặc xóa dữ liệu lịch sử.
