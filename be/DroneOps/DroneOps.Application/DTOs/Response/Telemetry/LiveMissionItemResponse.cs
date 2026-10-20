using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneOps.Application.DTOs.Response.Telemetry;

public class LiveMissionItemResponse
{
    public Guid MissionId { get; set; }
    public string MissionName { get; set; } = string.Empty;
    public string MissionStatus { get; set; } = string.Empty;
    public DateTime? StartTime { get; set; }

    // Thông tin UAV
    public Guid? UavId { get; set; }
    public string? UavCode { get; set; }
    public string? UavName { get; set; }

    // Thông tin Pilot phụ trách
    public Guid PilotId { get; set; }
    public string? PilotName { get; set; }

    // Snapshot viễn trắc mới nhất
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public decimal? AltitudeMeters { get; set; }
    public decimal? GroundSpeedMetersPerSecond { get; set; }
    public decimal? HeadingDegrees { get; set; }
    public int? BatteryPercent { get; set; }
    public decimal? BatteryVoltageVolts { get; set; }
    public decimal? BoardTemperatureCelsius { get; set; }
    public decimal? EnvironmentalTemperatureCelsius { get; set; }
    public decimal? RelativeHumidityPercent { get; set; }
    public string EnvironmentalSensorStatus { get; set; } = "Unavailable";
    public string? FlightMode { get; set; }
    public bool? Armed { get; set; }
    public int? MissionSequence { get; set; }

    // Tình trạng kết nối
    public DateTimeOffset? LastSeen { get; set; }
    public string ConnectivityStatus { get; set; } = "Offline"; // Online, Stale, Offline
    public double? DataAgeSeconds { get; set; }

    public int WaypointTotal { get; set; }

    public double? ProgressPercent { get; set; }
}
