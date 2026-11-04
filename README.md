# DroneOps Intelligence

Nền tảng lập kế hoạch, duyệt và giám sát nhiệm vụ UAV. Portal hỗ trợ người vận hành và quản lý từ đăng ký phi công, quản lý drone đến theo dõi telemetry và xử lý sau chuyến bay.

## Phạm vi chức năng của hồ sơ cuối

- Tài khoản, hồ sơ phi công, giấy phép, yêu cầu cập nhật và quy trình quản lý phê duyệt.
- Quản lý UAV, thông số kỹ thuật, hình ảnh và định danh flight controller.
- Lập nhiệm vụ theo waypoint, thời gian, tốc độ và độ cao; đánh giá kế hoạch với dữ liệu thời tiết, vùng hạn chế, địa hình và rủi ro pin.
- Đề xuất/duyệt thay đổi kế hoạch, phân biệt bản hiện hành và bản đề xuất, lưu lịch sử và bằng chứng validation.
- Kết nối drone qua gateway Python trên Jetson hoặc laptop với radio telemetry; ghép UID, đăng ký gateway tin cậy và cấp lease theo mission.
- Giám sát telemetry trực tiếp, cảnh báo/sự cố, thông báo và quy trình sau chuyến bay.
- **Bay tự động theo lịch:** duyệt lựa chọn `AutomaticFlightRequested`, biên dịch/upload/read-back mission, xác nhận pilot trước giờ bay, kiểm tra gate đúng giờ, gateway thực thi theo profile và ghi nhận trạng thái bay/completion từ telemetry.

Đặc tả hỗ trợ ArduPilot Copter và PX4 multicopter qua gateway tin cậy. Approval, consent, dispatch, ACK và trạng thái bay là các bước riêng; autopilot giữ điều khiển ổn định và failsafe onboard. Xem [quy trình vận hành bay tự động](docs/automatic-flight-operations.md). Đây là phạm vi đặc tả cuối; khi triển khai hoặc demo phải đối chiếu cấu hình và hành vi của bản runtime sử dụng.

## Kiến trúc

Phân công mainflow: MF1 — NghiaHQ (planning/preflight); MF2 — ThongLQ (automatic execution, realtime, incidents); MF3 — DucDDA (post-flight); MF4 — KhanhGNG (AI). ThongLQ giữ vai trò trưởng nhóm và điều phối hồ sơ.

```text
Portal Next.js → /api proxy → ASP.NET Core API → PostgreSQL
                                  ↑                 
Flight controller → MAVLink → Python gateway → HTTPS/WSS
                                  API → SSE → Portal
```

| Thư mục | Nội dung |
| --- | --- |
| `fe/` | Next.js 16, React 19, TypeScript; portal, bản đồ và API proxy |
| `be/DroneOps/` | .NET 8: API, Application, Domain, Persistence và các chương trình kiểm tra |
| `gateway/` | MAVLink gateway, launcher laptop, installer/service Jetson và đóng gói ZIP |
| `database/` | SQL migrations, thứ tự triển khai và cấu hình dữ liệu |
| `docs/` | Hướng dẫn vận hành và phạm vi chức năng; báo cáo tham khảo trong `reports/` |
| `deploy/`, `scripts/` | Cấu hình service, recovery và công cụ kiểm tra |

## Chạy local

Cần Node.js >=20.9, npm, .NET SDK 8 theo `global.json`, Python >=3.9 cho gateway và PostgreSQL đã áp dụng schema tương ứng.

1. Cấu hình backend bằng biến môi trường hoặc file không commit `be/DroneOps/DroneOps.API/appsettings.Local.json` khi chạy Development. Đặt `ConnectionStrings__DefaultConnection` cho database của môi trường của bạn; cấu hình JWT, SMTP, dịch vụ AI và storage theo tính năng cần chạy. Dùng đường dẫn certificate hợp lệ nếu kết nối database yêu cầu. Không đưa credential vào tài liệu hoặc commit.
2. Áp dụng migration có chủ đích theo [migration-order.txt](database/migration-order.txt), backup trước khi triển khai. Migration gateway/flight preparation là `20261104115300_gateway_enrollment_and_flight_preparation.sql`.
3. Chạy API từ thư mục gốc:

   ```powershell
   dotnet run --project be/DroneOps/DroneOps.API --no-launch-profile -- --urls http://localhost:5159 --environment Development
   ```

4. Trong `fe/`, sao chép `.env.example` thành `.env.local`, điền cấu hình Firebase và giữ `API_BASE_URL=http://localhost:5159` cho API local:

   ```powershell
   cd fe
   npm ci
   npm run dev
   ```

Mở `http://localhost:3000`. Frontend gọi API qua proxy cùng origin. Để nối drone thật, làm theo [hướng dẫn kết nối](docs/drone-connection-guide.md); hướng dẫn đó sử dụng domain production, cần chọn đúng môi trường khi thử local.

## Kiểm tra khi phát triển

Chạy các lệnh phù hợp với phần thay đổi, từ thư mục gốc trừ nhóm frontend:

```powershell
dotnet build be/DroneOps/DroneOps.sln
dotnet run --project be/DroneOps/DroneOps.AuthChecks
python -m pip install -r gateway/requirements.txt
python -m unittest discover -s gateway -p 'test_*.py'
```

Trong `fe/`:

```powershell
npm test
npm run typecheck
npm run lint
npm run build
```

Các kiểm tra tích hợp có ghi database/storage cần môi trường development; xem [AuthChecks](be/DroneOps/DroneOps.AuthChecks/README.md). Đọc `fe/AGENTS.md` và hướng dẫn Next.js cài trong dự án trước khi sửa frontend. Giao diện dùng nền navy, accent cyan và token dùng chung trong `tokens.css`; giữ trạng thái focus và hỗ trợ reduced motion.

## Tài liệu

- [Kết nối Jetson hoặc radio + laptop](docs/drone-connection-guide.md)
- [Vận hành nhiệm vụ bay tự động](docs/automatic-flight-operations.md)
- [Diagram cần cập nhật cho phạm vi mới](docs/diagram-update-checklist.md)
- [Gateway và đóng gói](gateway/README.md)
- [Media và hồ sơ phi công](database/USER_MEDIA.md)
- [Nguồn dữ liệu vùng trời](fe/public/data/README.md)
- [Báo cáo dự án](docs/reports/) và [slide](docs/slides/)

Khi làm việc với agent, bắt đầu từ README này và chỉ đọc tài liệu liên quan đến nhiệm vụ. Báo cáo/slide là tài liệu tham khảo theo thời điểm; đối chiếu code cho hành vi hiện tại. Cập nhật hướng dẫn đang có khi chức năng thay đổi, tránh tạo thêm nhật ký hoàn thành hoặc bản tóm tắt trùng nội dung.
