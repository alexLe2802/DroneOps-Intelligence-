namespace DroneOps.Application.DTOs.Response.Missions;

public sealed class MissionSuggestionData
{
    public string? MissionName { get; set; }

    public string? MissionPurpose { get; set; }

    public double? SuggestedAltitude { get; set; }

    public int? SuggestedWaypointCount { get; set; }

    public double? SuggestedFlightDuration { get; set; }

    // NEW
    public double? AverageSpeedMetersPerSecond { get; set; }

    public double? MaxSpeedMetersPerSecond { get; set; }

    public string? AltitudeReference { get; set; }

    public bool? AutomaticFlightRequested { get; set; }

    public List<MissionSuggestedWaypoint> Waypoints { get; set; } = [];

    public string? RiskAssessment { get; set; }

    public string? Recommendations { get; set; }

    public string? Summary { get; set; }
}

public sealed class MissionSuggestedWaypoint
{
    public int SequenceOrder { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public double Altitude { get; set; }

    public string? ActionType { get; set; }

    public MissionSuggestedNavigation? Navigation { get; set; }
}

public sealed class MissionSuggestedNavigation
{
    public double? SpeedMetersPerSecond { get; set; }

    public double? HoldTimeSeconds { get; set; }

    public double? AcceptanceRadiusMeters { get; set; }

    public double? PassRadiusMeters { get; set; }

    public double? YawDegrees { get; set; }

    public bool EmergencyLandingCandidate { get; set; }
}