using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneOps.Application.DTOs.Request.Telemetry;

public class IngestTelemetrySampleRequest
{
    public string SourceId { get; set; } = string.Empty;

    public Guid UavId { get; set; }

    public Guid? MissionId { get; set; }

    public DateTimeOffset SourceTimestamp { get; set; }

    public decimal Latitude { get; set; }

    public decimal Longitude { get; set; }

    public decimal? AltitudeMeters { get; set; }

    public string AltitudeReference { get; set; } = "Relative";

    public decimal? GroundSpeedMetersPerSecond { get; set; }

    public int? BatteryPercent { get; set; }

    public string? FlightMode { get; set; }

    public bool Armed { get; set; }

    public int? MissionSequence { get; set; }
}
