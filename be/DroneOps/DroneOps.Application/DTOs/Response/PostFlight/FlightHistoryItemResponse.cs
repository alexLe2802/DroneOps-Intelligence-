using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneOps.Application.DTOs.Response.PostFlight;

public class FlightHistoryItemResponse
{
    public Guid MissionId { get; set; }
    public string MissionName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? UavId { get; set; }
    public string UavName { get; set; } = string.Empty;
    public string UavCode { get; set; } = string.Empty;
    public string PilotName { get; set; } = string.Empty;
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public double DurationMinutes { get; set; }
    public int TotalIncidents { get; set; }
    public bool HasReport { get; set; }
}