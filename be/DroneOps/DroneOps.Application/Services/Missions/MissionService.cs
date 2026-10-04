using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Domain.Entities;

namespace DroneOps.Application.Services;

public sealed class MissionService : IMissionService
{
    private readonly IMissionRepository _missionRepository;

    public MissionService(IMissionRepository missionRepository)
    {
        _missionRepository = missionRepository;
    }

    public async Task<MissionResponse> CreateAsync(
        Guid pilotId,
        CreateMissionRequest request,
        CancellationToken cancellationToken = default)
    {
        var uavExists = await _missionRepository.UavExistsAsync(
            request.UavId,
            cancellationToken);

        if (!uavExists)
            throw new KeyNotFoundException("UAV was not found.");

        var now = DateTime.UtcNow;

        var mission = new Mission
        {
            Id = Guid.NewGuid(),
            CreatedBy = pilotId,
            UAVId = request.UavId,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description)
                ? null
                : request.Description.Trim(),
            Status = "Draft",
            StartTime = request.StartTime.ToUniversalTime(),
            EndTime = request.EndTime.ToUniversalTime(),
            CreatedAt = now
        };

        var missionVersion = new MissionVersion
        {
            Id = Guid.NewGuid(),
            MissionId = mission.Id,
            VersionNumber = 1,
            Status = "Draft",
            CreatedAt = now
        };

        foreach (var waypointRequest in request.Waypoints
                     .OrderBy(x => x.SequenceOrder))
        {
            missionVersion.Waypoints.Add(new Waypoint
            {
                Id = Guid.NewGuid(),
                MissionVersionId = missionVersion.Id,
                SequenceOrder = waypointRequest.SequenceOrder,
                Latitude = waypointRequest.Latitude,
                Longitude = waypointRequest.Longitude,
                Altitude = waypointRequest.Altitude,
                ActionType = string.IsNullOrWhiteSpace(
                    waypointRequest.ActionType)
                    ? null
                    : waypointRequest.ActionType.Trim(),
                CreatedAt = now
            });
        }

        mission.MissionVersions.Add(missionVersion);

        await _missionRepository.AddAsync(
            mission,
            cancellationToken);

        await _missionRepository.SaveChangesAsync(
            cancellationToken);

        return MapToResponse(mission, missionVersion);
    }

    private static MissionResponse MapToResponse(
        Mission mission,
        MissionVersion version)
    {
        return new MissionResponse
        {
            Id = mission.Id,
            CreatedBy = mission.CreatedBy,
            UavId = mission.UAVId,
            Name = mission.Name,
            Description = mission.Description,
            Status = mission.Status,
            StartTime = mission.StartTime,
            EndTime = mission.EndTime,
            CreatedAt = mission.CreatedAt,
            VersionNumber = version.VersionNumber,
            Waypoints = version.Waypoints
                .OrderBy(x => x.SequenceOrder)
                .Select(x => new WaypointResponse
                {
                    Id = x.Id,
                    SequenceOrder = x.SequenceOrder,
                    Latitude = x.Latitude,
                    Longitude = x.Longitude,
                    Altitude = x.Altitude,
                    ActionType = x.ActionType
                })
                .ToList()
        };
    }
}