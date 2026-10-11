using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneOps.Application.DTOs.Response.PostFlight;

public class FlightTelemetryPointResponse
{
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public decimal? Altitude { get; set; }
    public decimal? Speed { get; set; }
    public int? BatteryLevel { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
