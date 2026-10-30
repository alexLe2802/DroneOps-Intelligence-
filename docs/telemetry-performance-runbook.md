# KAN-52 and KAN-80 telemetry latency durability evidence

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
  "profile": {
    "id": "D-10-approved-profile-id",
    "approved": false,
    "deployment": "Describe API, database, gateway/load and browser hosts",
    "network": "Describe routes, regions, proxies and clock synchronization",
    "diagnosticsIntervalSeconds": 10,
    "maxManagedMemoryGrowthBytes": 104857600,
    "maxPendingThreadPoolWorkItems": 100
  },
  "vehicles": [
    { "sourceId": "perf-01", "uavId": "UUID", "missionId": "UUID" },
    { "sourceId": "perf-02", "uavId": "UUID", "missionId": "UUID" },
    { "sourceId": "perf-03", "uavId": "UUID", "missionId": "UUID" },
    { "sourceId": "perf-04", "uavId": "UUID", "missionId": "UUID" },
    { "sourceId": "perf-05", "uavId": "UUID", "missionId": "UUID" }
  ]
}
```

Các ngưỡng ví dụ không phải baseline được duyệt. Đặt `approved: true` và thay các
giá trị chỉ sau khi team xác nhận D-10/profile; runner cố ý không thể PASS nếu profile
chưa được duyệt hoặc thiếu ngưỡng queue/memory.

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
    browserSession: window.__KAN80_SESSION_ID,
    missionId: detail.missionId,
    sourceTimestamp: detail.sourceTimestamp,
    browserReceivedAt: detail.browserReceivedAt,
    renderedAt: detail.renderedAt,
    latencyMs: Date.parse(detail.renderedAt) - Date.parse(detail.sourceTimestamp),
    browserPaintLatencyMs: detail.browserReceivedAt
      ? Date.parse(detail.renderedAt) - Date.parse(detail.browserReceivedAt)
      : null
  };
  console.log("KAN80_RENDER " + JSON.stringify(row));
});
```

Đặt `window.__KAN80_SESSION_ID` thành một số riêng từ 1–10 trước khi đăng ký listener.
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
acknowledged record count, p50/p95/max cho từng stage, network ingress,
backend persist/publish, browser receive/paint, stage-correlation loss,
SSE/browser session count, incident-detection latency, managed memory, pending
ThreadPool work và subscriber cleanup.

Run chỉ PASS khi không mất request đã gửi, đủ 10 session, p95 SSE và browser đều
không quá 2000 ms, cả 10 browser session có evidence, không mất sample giữa
accepted→publish→render và có incident evidence. Đồng bộ NTP giữa
gateway/load host, API host và browser host; nếu không, source-to-render latency
không có giá trị. Không tuyên bố soak 60 phút PASS dựa trên smoke ngắn.

`GET /api/live/stream/diagnostics` chỉ cung cấp số liệu tiến trình tổng hợp, không
chứa mission hay telemetry. Runner chụp baseline trước khi mở observer SSE và chụp
lại sau khi đóng; `subscriberCleanupDelta` phải bằng 0. Báo cáo raw/summary phải
được lưu ngoài Git nếu chứa ID hoặc thông tin deployment nội bộ.
