# Jira Backend — Telemetry và thời tiết nhiệm vụ

Nguồn yêu cầu: `Report3_Software Requirement Specification.docx`, tập trung vào FE-05/UC-13/FR-13, FE-06/UC-14/FR-14, FE-07/UC-15/FR-15, FE-08/UC-16–18, BR-07–10, CR-01–07, NFR-R02 và NFR-P02.

Hiện trạng backend:

- `TelemetryRecord` mới lưu mission/UAV, tọa độ, độ cao, tốc độ, pin và `RecordedAt`.
- `GET /api/flight-history/{missionId}/telemetry` chỉ trả dữ liệu playback đã lưu.
- Chưa có gateway nhận MAVLink, live state, kênh realtime, trạng thái freshness/connectivity, mission progress hoặc API thời tiết.
- Dữ liệu thời tiết là dự báo theo waypoint, không được trộn với telemetry đo trực tiếp từ UAV.

---

## Task 1 — Xây dựng gateway nhận và lưu MAVLink telemetry

**Loại:** Story
**Độ ưu tiên:** Highest
**Nhãn:** `backend`, `telemetry`, `mavlink`, `uc-13`

### Mục tiêu

Nhận telemetry từ UAV/autopilot hoặc simulator đã được cấu hình, chuẩn hóa dữ liệu, liên kết đúng UAV/mission và lưu bằng chứng telemetry phục vụ live monitoring và post-flight.

### Phạm vi

- Chốt các quyết định D-05 trong SRS:
  - Danh sách MAVLink message được hỗ trợ.
  - Cách ánh xạ nguồn gửi với UAV và mission/flight.
  - Tần suất lấy mẫu, retry và deduplication.
  - `sourceTimestamp`, `receivedAt`, clock skew và late arrival.
  - Chuẩn độ cao: MSL, Relative hoặc AGL.
  - Sự kiện bắt đầu/kết thúc chuyến bay; mất kết nối không được tự động xem là hoàn thành.
- Tối thiểu hỗ trợ các nhóm dữ liệu:
  - `HEARTBEAT`: trạng thái kết nối, flight mode, armed state.
  - `GLOBAL_POSITION_INT`: latitude, longitude và altitude.
  - `VFR_HUD`: ground speed.
  - `SYS_STATUS` hoặc `BATTERY_STATUS`: battery percentage.
  - `MISSION_CURRENT`: waypoint hiện tại và tiến độ nhiệm vụ.
- Tạo gateway nội bộ có xác thực riêng cho integration, không sử dụng phiên đăng nhập của người dùng.
- Mở rộng `TelemetryRecord` và migration database để lưu:
  - Source ID, UAV ID, mission/flight ID.
  - Source timestamp và server received timestamp.
  - Latitude, longitude, altitude, altitude reference.
  - Ground speed, battery, flight mode, armed state và mission sequence.
  - Trạng thái hợp lệ và khóa deduplication.
- Duy trì latest valid state cho mỗi UAV/active flight.

### API đề xuất

```http
POST /api/integrations/telemetry/samples
Authorization: integration credential
Idempotency-Key: {sourceId}:{messageSequence}:{sourceTimestamp}
```

```json
{
  "sourceId": "sim-uav-001",
  "uavId": "uuid",
  "missionId": "uuid-or-null",
  "sourceTimestamp": "2026-10-13T10:00:00.000Z",
  "latitude": 10.849,
  "longitude": 106.772,
  "altitudeMeters": 82.4,
  "altitudeReference": "Relative",
  "groundSpeedMetersPerSecond": 12.6,
  "batteryPercent": 74,
  "flightMode": "AUTO",
  "armed": true,
  "missionSequence": 3
}
```

### Tiêu chí chấp nhận

- Nguồn không hợp lệ hoặc không ánh xạ được UAV phải bị từ chối.
- Kiểm tra range tọa độ, pin và các trường số tại backend.
- Packet sai hoặc không hỗ trợ được ghi log nhưng không làm chết stream.
- Retry cùng idempotency key chỉ tạo một telemetry sample.
- Giá trị thiếu phải lưu `null`, không được chuyển thành `0`.
- Sample đến trễ có thể được giữ làm lịch sử nhưng không được ghi đè latest state mới hơn.
- Migration phải giữ nguyên telemetry cũ.
- Có index cho truy vấn playback theo mission và latest state theo UAV.
- Không ghi integration credential vào log.
- Có integration test cho sample hợp lệ, trùng lặp, sai dữ liệu, sai nguồn và out-of-order.

---

## Task 2 — Xây dựng live monitoring, incident và post-flight telemetry API

**Loại:** Story
**Độ ưu tiên:** Highest
**Phụ thuộc:** Task 1
**Nhãn:** `backend`, `live-monitoring`, `incidents`, `post-flight`, `uc-14`, `uc-15`, `uc-16`

### Mục tiêu

Cung cấp dữ liệu telemetry realtime cho Pilot và Operations Manager, phát hiện tình trạng bất thường, quản lý incident và hỗ trợ playback/post-flight theo đúng SRS.

### Phạm vi

- API snapshot và danh sách mission đang hoạt động.
- WebSocket hoặc SignalR để gửi telemetry mới đến client được phép xem.
- Live state phải gồm đúng các chỉ số SRS yêu cầu:
  - Vị trí.
  - Độ cao.
  - Tốc độ.
  - Pin.
  - Connectivity.
  - Sample freshness.
  - Mission progress.
  - Source timestamp và received timestamp.
  - Incident đang mở.
- Background watchdog chạy ngay cả khi không có packet để xác định `Live`, `Stale`, `Offline` hoặc `Unavailable`.
- Chốt D-06 và cấu hình rule cho:
  - Communication loss.
  - Low battery.
  - Altitude bất thường.
  - Speed bất thường.
  - Severity, hysteresis, cooldown và duplicate suppression.
- API danh sách/chi tiết incident, acknowledge và resolve.
- Nâng cấp telemetry playback để hỗ trợ chuyến bay dài, missing data và planned-versus-recorded route.

### API đề xuất

```http
GET /api/live-monitoring/missions
GET /api/live-monitoring/missions/{missionId}/state
WebSocket hoặc SignalR: /hubs/telemetry
```

```http
GET  /api/incidents?missionId=&uavId=&type=&severity=&status=&from=&to=&pageNumber=&pageSize=
GET  /api/incidents/{incidentId}
POST /api/incidents/{incidentId}/acknowledge
POST /api/incidents/{incidentId}/resolve
```

```http
GET /api/flight-history/{missionId}/telemetry?from=&to=&maxPoints=&cursor=
```

### Tiêu chí chấp nhận

- Pilot chỉ xem được mission thuộc phạm vi sở hữu/phân công; Manager xem phạm vi quản lý.
- Snapshot luôn trả trạng thái freshness và `lastUpdatedAt`.
- Khi stale/offline, API vẫn có thể trả last-known value nhưng phải đánh dấu rõ là dữ liệu cũ.
- Khi reconnect, client nhận latest valid snapshot trước khi nhận event mới.
- Mission progress trả theo waypoint/mission sequence; nếu không có dữ liệu phải trả `null`.
- Watchdog chuyển stale/offline trong ngưỡng cấu hình cộng tối đa 2 giây theo NFR-R02.
- Dữ liệu reconnect xuất hiện trong tối đa 5 giây sau khi sample hợp lệ được chấp nhận.
- 95% sample hợp lệ đến live client trong 2 giây theo NFR-P02 và profile P-01.
- Mất kết nối không được tự động hoàn thành mission.
- Incident lưu type, severity, UAV/mission, thời gian nguồn, thời gian phát hiện, rule/threshold và telemetry evidence.
- Thiếu một metric chỉ bỏ qua rule của metric đó, không làm dừng các rule khác.
- Một tình trạng kéo dài không được tạo incident không giới hạn.
- Acknowledge/resolve phải lưu actor, thời gian và ghi chú; không sửa telemetry gốc.
- Playback có phân trang hoặc downsampling, sample count, khoảng thời gian, validity và các khoảng thiếu dữ liệu.
- Post-flight aggregation bỏ qua giá trị `null`, không xem `null` là `0`.
- Có test phân quyền, stale/offline, reconnect, duplicate incident, concurrent status update và hiệu năng P-01.

---

## Task 3 — Tích hợp thời tiết thật theo waypoint của mission

**Loại:** Story / SRS change request
**Độ ưu tiên:** High
**Phụ thuộc:** Task 1 và API mission detail có ordered waypoints
**Nhãn:** `backend`, `weather`, `waypoints`, `external-api`, `dashboard`

### Mục tiêu

Lấy dự báo thời tiết cho từng khu vực và thời điểm mà UAV dự kiến đi qua, thay cho một giá trị thời tiết chung của thành phố. Dữ liệu phải phục vụ mission planning, AI assessment và dashboard.

Đây là dữ liệu từ nhà cung cấp dự báo, không phải telemetry đo trực tiếp từ UAV. Response và database/cache phải ghi rõ nguồn dữ liệu.

### Dữ liệu thời tiết cần lấy

Cho mỗi waypoint hoặc nhóm waypoint gần nhau:

- Nhiệt độ tại 2 m: `temperatureCelsius`.
- Độ ẩm tương đối tại 2 m: `relativeHumidityPercent`.
- Tốc độ gió: `windSpeed`.
- Hướng gió: `windDirectionDegrees`.
- Gió giật tại 10 m: `windGust`.
- Xác suất mưa: `precipitationProbabilityPercent`.
- Lượng mưa: `precipitationMillimeters`.
- Tầm nhìn: `visibilityMeters`.
- Độ phủ mây: `cloudCoverPercent`.
- Weather code/type.
- Forecast timestamp, thời điểm lấy dữ liệu, provider và model/run metadata nếu nhà cung cấp có trả.
- Nếu provider có dữ liệu gió theo độ cao phù hợp thì dùng độ cao waypoint; nếu chỉ có gió bề mặt 10 m phải ghi rõ `windReferenceHeightMeters: 10`.

Open-Meteo có thể dùng làm provider đầu tiên, nhưng cần thiết kế adapter để có thể thay bằng Tomorrow.io hoặc provider khác.

### API đề xuất

```http
GET /api/missions/{missionId}/weather
POST /api/missions/{missionId}/weather/refresh
```

Response tối thiểu:

```json
{
  "missionId": "uuid",
  "status": "Available",
  "provider": "OpenMeteo",
  "generatedAt": "2026-10-13T10:00:00Z",
  "expiresAt": "2026-10-13T10:15:00Z",
  "flightWindow": {
    "startTime": "2026-10-14T02:00:00Z",
    "endTime": "2026-10-14T03:00:00Z"
  },
  "overallRisk": "Moderate",
  "waypoints": [
    {
      "sequenceOrder": 1,
      "latitude": 10.849,
      "longitude": 106.772,
      "altitudeMeters": 80,
      "estimatedArrivalTime": "2026-10-14T02:05:00Z",
      "forecastTime": "2026-10-14T02:00:00Z",
      "temperatureCelsius": 29.4,
      "relativeHumidityPercent": 78,
      "windSpeedKilometersPerHour": 18,
      "windDirectionDegrees": 210,
      "windGustKilometersPerHour": 31,
      "windReferenceHeightMeters": 10,
      "precipitationProbabilityPercent": 45,
      "precipitationMillimeters": 0.4,
      "visibilityMeters": 9000,
      "cloudCoverPercent": 66,
      "weatherCode": 61,
      "riskLevel": "Moderate",
      "warnings": [
        "Wind gust exceeds configured UAV operating limit"
      ]
    }
  ]
}
```

### Tiêu chí chấp nhận

- API key và cấu hình provider chỉ nằm ở backend.
- Dùng tọa độ waypoint cùng thời gian bắt đầu/kết thúc mission để chọn forecast.
- Backend ước lượng thời điểm UAV đến từng waypoint; nếu không thể ước lượng phải trả `null` và nêu lý do.
- Không sử dụng một current-weather value cho toàn bộ route.
- Gom waypoint gần nhau và cache theo vùng/thời gian để giảm số lần gọi provider.
- Response hỗ trợ `Available`, `Partial`, `Unavailable` và `Stale`.
- Provider timeout/rate limit không được làm mất khả năng mở mission detail hoặc review thủ công.
- Trường không có dữ liệu phải trả `null` kèm unit/source rõ ràng.
- Risk/warning phải dựa trên giới hạn vận hành được cấu hình theo UAV hoặc chính sách đã duyệt; không hard-code thành quy định hàng không.
- Mỗi warning chỉ rõ waypoint, giá trị dự báo, threshold và unit.
- Đổi route hoặc thời gian bay phải làm mất hiệu lực cache cũ.
- Pilot chỉ xem mission thuộc phạm vi; Manager xem phạm vi quản lý.
- Có test bằng provider stub cho đủ dữ liệu, thiếu một phần, timeout, rate limit và thời gian nằm ngoài forecast window.
