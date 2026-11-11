namespace DroneOps.Application.DTOs.Response.AI;

public sealed class AiAssessmentResponse
{
    public Guid Id { get; set; }
    public string MissionRef { get; set; } = string.Empty;
    public int MissionVersion { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ContextHash { get; set; } = string.Empty;
    public bool IsOutdated { get; set; }
    public string? OutdatedReason { get; set; }
    public AiAssessmentResultResponse? Result { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class AiAssessmentResultResponse
{
    public DroneOps.Domain.Entities.AutomaticFlightLimits? AutomaticFlightLimits { get; set; }
    public decimal? CruiseSpeedMetersPerSecond { get; set; }
    public decimal? MaxSpeedMetersPerSecond { get; set; }
    public DroneOps.Application.Services.Missions.AutomaticRecoveryContract? RecoveryPolicy { get; set; }
    public BatteryAssessmentResponse? Battery { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<AiRiskResponse> Risks { get; set; } = [];
    public List<string> Precautions { get; set; } = [];
    public List<string> MissingData { get; set; } = [];
    public string Disclaimer { get; set; } = string.Empty;
    public SimilarityMethodResponse Retrieval { get; set; } = new();
    public List<SimilarFlightResponse> HistoricalFlights { get; set; } = [];
}

public sealed class AiRiskResponse
{
    public string Severity { get; set; } = "Moderate";
    public string Title { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public string Precaution { get; set; } = string.Empty;
}

public sealed class SimilarityMethodResponse
{
    public string Method { get; set; } = "normalized-euclidean";
    public string FeatureSchema { get; set; } = "route-centroid-duration-altitude-uav-incident-v1";
    public int MaximumResults { get; set; } = 5;
}

public sealed class SimilarFlightResponse
{
    public Guid MissionId { get; set; }
    public string MissionName { get; set; } = string.Empty;
    public double SimilarityScore { get; set; }
    public Dictionary<string, double?> Features { get; set; } = [];
    public List<Guid> IncidentIds { get; set; } = [];
}
