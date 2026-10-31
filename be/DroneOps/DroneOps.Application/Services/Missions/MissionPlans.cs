using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Domain.Entities;

namespace DroneOps.Application.Services.Missions;

public static class MissionPlans
{
    private static DateTime StoredTime(DateTime value)
    {
        var utc = value.ToUniversalTime();

        return new DateTime(
            utc.Ticks / 10 * 10,
            DateTimeKind.Utc);
    }

    public static Mission Propose(
        Mission current,
        CreateMissionRequest request)
    {
        return new Mission
        {
            Id = current.Id,
            CreatedBy = current.CreatedBy,
            CreatedAt = current.CreatedAt,
            Status = current.Status,

            UAVId = request.UavId,

            Name = request.Name.Trim(),
            Description = request.Description,

            AltitudeReference =
                request.AltitudeReference,

            StartTime =
                StoredTime(request.StartTime),

            EndTime =
                StoredTime(request.EndTime),

            AverageSpeedMetersPerSecond =
    request.AverageSpeedMetersPerSecond,

            MaxSpeedMetersPerSecond =
    request.MaxSpeedMetersPerSecond,

            Waypoints = request.Waypoints
                .Select(x => new Waypoint
                {
                    Id = Guid.NewGuid(),

                    MissionId = current.Id,

                    SequenceOrder =
                        x.SequenceOrder,

                    Latitude = decimal.Round(
                        x.Latitude,
                        7,
                        MidpointRounding.AwayFromZero),

                    Longitude = decimal.Round(
                        x.Longitude,
                        7,
                        MidpointRounding.AwayFromZero),

                    Altitude = x.Altitude.HasValue
                        ? decimal.Round(
                            x.Altitude.Value,
                            2,
                            MidpointRounding.AwayFromZero)
                        : null,

                    ActionType =
                        x.ActionType ?? "FlyThrough",

                    CreatedAt =
                        DateTime.UtcNow
                })
                .ToList()
        };
    }

    public static CreateMissionRequest Dto(
        Mission mission)
    {
        return new CreateMissionRequest
        {
            UavId =
                mission.UAVId ?? Guid.Empty,

            Name =
                mission.Name,

            Description =
                mission.Description,

            AltitudeReference =
                mission.AltitudeReference,

            StartTime =
                mission.StartTime ?? default,

            EndTime =
                mission.EndTime ?? default,

            AverageSpeedMetersPerSecond =
    mission.AverageSpeedMetersPerSecond ?? 0,

            MaxSpeedMetersPerSecond =
    mission.MaxSpeedMetersPerSecond ?? 0,

            Waypoints = mission.Waypoints
                .OrderBy(x => x.SequenceOrder)
                .Select(x => new CreateWaypointRequest
                {
                    SequenceOrder =
                        x.SequenceOrder,

                    Latitude = decimal.Round(
                        x.Latitude,
                        7,
                        MidpointRounding.AwayFromZero),

                    Longitude = decimal.Round(
                        x.Longitude,
                        7,
                        MidpointRounding.AwayFromZero),

                    Altitude = x.Altitude.HasValue
                        ? decimal.Round(
                            x.Altitude.Value,
                            2,
                            MidpointRounding.AwayFromZero)
                        : null,

                    ActionType =
                        x.ActionType
                })
                .ToList()
        };
    }
}