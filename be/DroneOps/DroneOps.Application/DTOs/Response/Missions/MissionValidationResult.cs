namespace DroneOps.Application.DTOs.Response.Missions;

public sealed class MissionValidationResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UpdateRequestId { get; set; }
    public string RestrictionRevision { get; set; } = "";
    public bool Stale { get; set; }
    public bool Incomplete { get; set; }
    public List<RestrictionFinding> Findings { get; set; } = [];
    public Guid? MissionId { get; set; }
    public string Stage { get; set; } = "Creation";
    public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsValid => Issues.Count == 0 && !Incomplete && !Stale;
    public string Status => Stale ? "Stale" : Incomplete ? "Incomplete" : IsValid ? "Passed" : "Blocked";
    public string Message => IsValid ? "Mandatory checks passed. Weather warnings are advisory." : string.Join(" ", Issues);
    public List<string> Issues { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public WeatherAdvisory? Weather { get; set; }
}

public sealed class WeatherAdvisory
{
    public Guid? MissionId { get; set; }
    public Guid? UpdateRequestId { get; set; }
    public string PlanHash { get; set; } = "";
    public string Status { get; set; } = "Unavailable";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(10);
    public DateTime? FlightStartTime { get; set; }
    public DateTime? FlightEndTime { get; set; }
    public List<WaypointWeather> Waypoints { get; set; } = [];
    public string OverallRisk { get; set; } = "Unknown";
    public string? ModelRun { get; set; }
    public string Provider { get; set; } = "Open-Meteo";
    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Available { get; set; }
    public decimal? MaxWindMetersPerSecond { get; set; }
    public decimal? MaxGustMetersPerSecond { get; set; }
    public decimal? MaxRainMillimetersPerHour { get; set; }
    public bool ThunderstormForecast { get; set; }
    public List<string> Warnings { get; set; } = [];
}

public sealed class MissionValidationException(MissionValidationResult result) : ArgumentException(result.Message)
{
    public MissionValidationResult Result { get; } = result;
}

public sealed class RestrictionFinding
{
    public Guid GeofenceId { get; set; }
    public long Revision { get; set; }
    public string Rule { get; set; } = "AirspaceIntersection";
    public int? WaypointSequence { get; set; }
    public int? SegmentFrom { get; set; }
    public int? SegmentTo { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class WaypointWeather
{
    public int SequenceOrder { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public decimal? AltitudeMeters { get; set; }
    public DateTime? EstimatedArrivalTime { get; set; }
    public string? ArrivalTimeReason { get; set; }
    public DateTime? ForecastTime { get; set; }
    public DateTimeOffset? SourceFetchedAt { get; set; }
    public decimal? TemperatureCelsius { get; set; }
    public decimal? RelativeHumidityPercent { get; set; }
    public decimal? WindSpeedMetersPerSecond { get; set; }
    public decimal? WindDirectionDegrees { get; set; }
    public decimal? WindGustMetersPerSecond { get; set; }
    public int WindReferenceHeightMeters { get; set; } = 10;
    public decimal? PrecipitationProbabilityPercent { get; set; }
    public decimal? PrecipitationMillimeters { get; set; }
    public decimal? VisibilityMeters { get; set; }
    public decimal? CloudCoverPercent { get; set; }
    public int? WeatherCode { get; set; }
    public string Status { get; set; } = "Unavailable";
    public string RiskLevel { get; set; } = "Unknown";
    public List<string> Warnings { get; set; } = [];
}
