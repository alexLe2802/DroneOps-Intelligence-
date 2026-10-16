using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneOps.Application.DTOs.Response.Telemetry;

public class MissionLatestTelemetryResponse
{
    public Guid MissionId { get; set; }
    public Guid? UavId { get; set; }
    public string? UavCode { get; set; }
    public string? UavName { get; set; }
    public string? SourceId { get; set; }

    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public decimal? AltitudeMeters { get; set; }
    public string? AltitudeReference { get; set; } = "Relative";
    public decimal? GroundSpeedMetersPerSecond { get; set; }
    public int? BatteryPercent { get; set; }
    public string? FlightMode { get; set; }
    public bool Armed { get; set; }
    public int? MissionSequence { get; set; }

    public DateTimeOffset? SourceTimestamp { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }

    // Tình trạng kết nối và độ tươi mới của dữ liệu
    public string ConnectivityStatus { get; set; } = "Offline"; // Online, Stale, Offline
    public double? DataAgeSeconds { get; set; }
    public bool IsStale { get; set; } = true;
    public bool IsValid { get; set; }

    /// <summary>
    /// Missing | Latest | Stale | Offline — phân biệt snapshot rỗng với dữ liệu cũ, không dùng playback.
    /// </summary>
    public string SnapshotKind { get; set; } = "Missing";

    public int WaypointTotal { get; set; }

    /// <summary>
    /// Tiến độ D-05: missionSequence / tổng waypoint, kẹp 0–100; không dùng Completed khi mất kết nối.
    /// </summary>
    public double? ProgressPercent { get; set; }
}
