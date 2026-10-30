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
    public string CompletionRateStatus { get; set; } = "Unavailable";
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
    public AnalyticsContractResponse Contract { get; set; } = new();
    public AnalyticsCoverageResponse MissingCoverage { get; set; } = new();
}

public sealed class AnalyticsContractResponse
{
    public string WindowBasis { get; set; } = "Mission.StartTime";
    public string WindowTimeZone { get; set; } = "UTC";
    public Dictionary<string, string> Units { get; set; } = new()
    {
        ["totalFlightHours"] = "hours", ["totalDistanceKilometers"] = "kilometers",
        ["rates"] = "percent", ["excludedGapSeconds"] = "seconds"
    };
    public Dictionary<string, string> SourceLinks { get; set; } = new()
    {
        ["missions"] = "/api/flight-history", ["telemetry"] = "/api/flight-history/{missionId}/telemetry",
        ["incidents"] = "/api/flight-history/{missionId}/incidents"
    };
    public int CommunicationLossThresholdSeconds { get; set; }
}

public sealed class AnalyticsCoverageResponse
{
    public int ValidDistanceSegments { get; set; }
    public int ExcludedDistanceSegments { get; set; }
    public double ExcludedGapSeconds { get; set; }
    public int MissingGpsSamples { get; set; }
    public int InvalidTelemetrySamples { get; set; }
    public int MissionsWithoutValidGpsCoverage { get; set; }
    public int MissionsWithoutAvailableReport { get; set; }
}

public sealed class AnalyticsTrendPointResponse
{
    public DateOnly Date { get; set; }
    public int TotalFlights { get; set; }
    public int CompletedFlights { get; set; }
    public int FailedOrAbortedFlights { get; set; }
}
