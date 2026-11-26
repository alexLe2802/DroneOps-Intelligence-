using DroneOps.Domain.Entities;

namespace DroneOps.Application.DTOs.Request.Missions;

public sealed record DraftAnalysisRequest(Guid UavId, string Name, DateTimeOffset StartTime,
    DateTimeOffset? EndTime, List<DraftWaypoint> Waypoints, bool? Automatic,
    AutomaticFlightLimits? Limits, decimal? Cruise, decimal? Ceiling, decimal? PayloadKg, string? AltitudeReference);

public sealed record DraftWaypoint(int SequenceOrder, decimal? Latitude, decimal? Longitude, decimal? Altitude,
    string? ActionType, WaypointNavigation? Navigation);
