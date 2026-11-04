# DroneOps gateway

Hướng dẫn kết nối production: [Kết nối drone](../docs/drone-connection-guide.md).
Trong ZIP tải về, mở `CONNECTION_SETUP.md`.

- Jetson: chạy lệnh setup từ web một lần; systemd tự nối khi bật nguồn.
- Radio + laptop: Windows mở `connect-drone.cmd`; macOS/Linux chạy
  `bash connect-drone.sh`. Launcher dò USB/baud/ArduPilot hoặc PX4 và dùng domain
  backend cố định. Giữ cửa sổ mở trong lúc nhận telemetry.
- UID lấy từ flight controller, không lấy từ Jetson hoặc radio.
- Gateway mặc định chỉ truyền telemetry. Có executor thử nghiệm bị khóa cho
  upload/read-back và ARM/DISARM/mission start; chưa bật trên Jetson/production.
  Chưa được dùng hai cờ opt-in để bay thật trước khi hoàn thành các gate trong
  [Đề xuất bay tự động](../docs/automatic-flight-proposal.md).

Developer verification:

```bash
python -m pip install -r gateway/requirements.txt
python -m unittest discover -s gateway -p 'test_*.py'
python gateway/package.py
```

Không commit file credential. Session mission hết hạn sau 12 giờ; trusted device
credential có thể xin lease mới cho connection hợp lệ được chủ UAV chọn trên web.
`DISCONNECT` không hồi sinh session; `FORGET TRUSTED GATEWAY` thu hồi đăng ký.
