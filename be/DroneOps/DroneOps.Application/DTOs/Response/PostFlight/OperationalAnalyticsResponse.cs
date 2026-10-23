using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneOps.Application.DTOs.Response.PostFlight;

public sealed class OperationalAnalyticsResponse
{
    public int TotalFlights { get; set; }
    public int CompletedFlights { get; set; }
    public int AbortedOrFailedFlights { get; set; }
    public double TotalFlightHours { get; set; }
    public decimal TotalDistanceKilometers { get; set; }
    public int TotalIncidentsRecorded { get; set; }
    public double? SuccessRatePercentage { get; set; }
    public int ReportsAvailable { get; set; }
    public int ReportsExcluded { get; set; }
    public double? ReportCoveragePercentage { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public Guid? UavId { get; set; }
    public Guid? MissionId { get; set; }
    public List<AnalyticsTrendPointResponse> CompletionTrend { get; set; } = [];
    public Dictionary<string, int> IncidentSeverityBreakdown { get; set; } = new();
    public Dictionary<string, int> ExcludedReportReasons { get; set; } = new();
}

public sealed class AnalyticsTrendPointResponse
{
    public DateOnly Date { get; set; }
    public int TotalFlights { get; set; }
    public int CompletedFlights { get; set; }
    public int FailedOrAbortedFlights { get; set; }
}
