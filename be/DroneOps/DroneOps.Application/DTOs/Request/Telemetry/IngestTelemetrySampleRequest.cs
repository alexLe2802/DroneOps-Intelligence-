using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneOps.Application.DTOs.Request.Telemetry;

public class IngestTelemetrySampleRequest
{
    public string SourceId { get; set; } = string.Empty;
    public string? SourceBootId { get; set; }
    public string? SourceEventId { get; set; }
    public DateTimeOffset? BatteryObservedAt { get; set; }
    public DateTimeOffset? AltitudeObservedAt { get; set; }
    public DateTimeOffset? SpeedObservedAt { get; set; }

    public Guid UavId { get; set; }

    public Guid? MissionId { get; set; }

    public DateTimeOffset SourceTimestamp { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public decimal? AltitudeMeters { get; set; }

    public decimal? AltitudeMslMeters { get; set; }

    public string AltitudeReference { get; set; } = "Relative";

    public decimal? GroundSpeedMetersPerSecond { get; set; }

    public decimal? HeadingDegrees { get; set; }

    public int? BatteryPercent { get; set; }

    public decimal? BatteryVoltageVolts { get; set; }
    public decimal? BatteryCurrentAmps { get; set; }
    public decimal? BatteryTemperatureCelsius { get; set; }
    public int? BatteryConsumedMah { get; set; }
    public decimal[]? BatteryCellVoltagesVolts { get; set; }

    /// <summary>PX4/autopilot board temperature, not ambient weather.</summary>
    public decimal? BoardTemperatureCelsius { get; set; }

    public string? FlightMode { get; set; }

    public bool? Armed { get; set; }

    public int? MissionSequence { get; set; }
}
