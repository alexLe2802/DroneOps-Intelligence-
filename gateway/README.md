# DroneOps onboard gateway

Hướng dẫn sử dụng duy nhất: [Kết nối drone và Jetson](../docs/drone-connection-guide.md).
Trong ZIP tải về, mở **CONNECTION_SETUP.md**.

Pixhawk -> MAVLink Router -> UDP localhost -> gateway -> WSS backend -> SSE web.
Router tùy chọn gửi MAVLink đến QGroundControl laptop. Profile radio không có Jetson
chạy gateway trực tiếp trên laptop qua serial của ground radio hoặc UDP forwarding.
Không cần camera. Xem phần profile radio trong hướng dẫn chung.

Cài một lần bằng `sudo bash install-jetson.sh`. Tạo mã trên Connect Drone và chạy
`sudo /opt/droneops-gateway/pair-jetson.sh`. Service tự chạy khi boot và tự nối lại
mạng. Phiên hết hạn 12 giờ, revoke hoặc đổi mission cần pair lại.

Developer checks:

```sh
python -m pip install -r gateway/requirements.txt
python -m unittest discover -s gateway -p 'test_*.py'
python gateway/package.py
dotnet run --project be/DroneOps/DroneOps.TelemetryChecks -- --gateway /absolute/repo
```

Đặt DRONEOPS_GATEWAY_PYTHON trỏ đến Python có dependencies cho loopback gateway.
PTY chỉ chạy POSIX; loopback không chứng minh phần cứng thật. Không commit token.
