# KAN-80 — Telemetry throughput, latency and incident evidence

Runner này tạo đúng 5 UAV mô phỏng, mỗi UAV 1 sample/giây, mở mặc định 10 SSE
session và ghi từng observation vào JSONL. Mặc định chạy 3600 giây; không giảm thời
gian khi lấy bằng chứng soak chính thức.

## Cấu hình riêng

Tạo file ngoài Git, ví dụ `telemetry-performance.private.json`:

```json
{
  "apiBase": "https://backend.example.test",
  "integrationKey": "REDACTED",
  "viewerToken": "REDACTED",
  "vehicles": [
    { "sourceId": "perf-01", "uavId": "UUID", "missionId": "UUID" },
    { "sourceId": "perf-02", "uavId": "UUID", "missionId": "UUID" },
    { "sourceId": "perf-03", "uavId": "UUID", "missionId": "UUID" },
    { "sourceId": "perf-04", "uavId": "UUID", "missionId": "UUID" },
    { "sourceId": "perf-05", "uavId": "UUID", "missionId": "UUID" }
  ]
}
```

Năm mission phải ở trạng thái active/in-progress, map đúng UAV và token người xem
phải có quyền xem cả năm. Không commit file này hay raw evidence chứa ID nội bộ.

## Browser render evidence

Live Monitoring phát local event `droneops:telemetry-rendered` ở animation frame
sau khi React commit telemetry. Browser automation phải đăng ký listener trước khi
mở màn hình và ghi JSONL dạng:

```js
window.addEventListener("droneops:telemetry-rendered", event => {
  const detail = event.detail;
  const row = {
    kind: "telemetry-rendered",
    missionId: detail.missionId,
    sourceTimestamp: detail.sourceTimestamp,
    renderedAt: detail.renderedAt,
    latencyMs: Date.parse(detail.renderedAt) - Date.parse(detail.sourceTimestamp)
  };
  console.log("KAN80_RENDER " + JSON.stringify(row));
});
```

Thu các dòng sau tiền tố `KAN80_RENDER` của 10 browser page/session vào một file
JSONL. Đây là mốc render thật; không đổi tên SSE receipt thành browser latency.

## Chạy

Smoke ngắn để kiểm tra cấu hình:

```powershell
node --test scripts/telemetry-performance.test.mjs
node scripts/telemetry-performance.mjs --config telemetry-performance.private.json `
  --duration-seconds 30 --browser-evidence artifacts/browser-render.jsonl
```

Soak nghiệm thu 60 phút:

```powershell
node scripts/telemetry-performance.mjs --config telemetry-performance.private.json `
  --duration-seconds 3600 --sessions 10 `
  --browser-evidence artifacts/browser-render.jsonl `
  --output artifacts/kan-80-soak
```

Runner chủ động tạo duplicate mỗi 20 giây, late sample mỗi 30 giây, low-battery
incident và một cửa sổ disconnect/reconnect. `raw-evidence.jsonl` giữ timestamp,
HTTP status, latency và event; `summary.json` có accepted/lost/late/duplicate,
persist/SSE/browser p95, session count và incident-detection latency.

Run chỉ PASS khi không mất request đã gửi, đủ 10 session, p95 SSE và browser đều
không quá 2000 ms, có browser evidence và có incident evidence. Đồng bộ NTP giữa
gateway/load host, API host và browser host; nếu không, source-to-render latency
không có giá trị. Không tuyên bố soak 60 phút PASS dựa trên smoke ngắn.
