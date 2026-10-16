using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;

namespace DroneOps.Application.Services;

public sealed class MissionService : IMissionService
{
    private readonly IMissionRepository _missionRepository;
    private readonly MissionBusinessValidator _businessValidator;

    public MissionService(
       IMissionRepository missionRepository,
       MissionBusinessValidator businessValidator)
    {
        _missionRepository = missionRepository;
        _businessValidator = businessValidator;
    }
    public async Task<MissionResponse> CreateAsync(
        Guid pilotId,
        CreateMissionRequest request,
        CancellationToken cancellationToken = default)
    {
        await _businessValidator.ValidateUavAsync(
     request.UavId,
     pilotId,
     cancellationToken);

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

        foreach (var waypointRequest in request.Waypoints
                     .OrderBy(x => x.SequenceOrder))
        {
            mission.Waypoints.Add(new Waypoint
            {
                Id = Guid.NewGuid(),

                MissionId = mission.Id,

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

        await _missionRepository.AddAsync(
            mission,
            cancellationToken);

        await _missionRepository.SaveChangesAsync(
            cancellationToken);

        return MapToResponse(mission);
    }
    public async Task<MissionResponse?> GetByIdAsync(
    Guid missionId,
    CancellationToken cancellationToken = default)
    {
        Mission? mission =
            await _missionRepository.GetByIdAsync(
                missionId,
                cancellationToken);

        if (mission is null)
        {
            return null;
        }

        return MapToResponse(mission);
    }
    public async Task<List<MissionResponse>>
    GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        List<Mission> missions =
            await _missionRepository.GetAllAsync(
                cancellationToken);

        return missions
            .Select(MapToResponse)
            .ToList();
    }

    public async Task<List<MissionResponse>>
    GetMyAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        List<Mission> missions =
            await _missionRepository.GetByCreatorAsync(
                userId,
                cancellationToken);

        return missions
            .Select(MapToResponse)
            .ToList();
    }
    private static MissionResponse MapToResponse(
        Mission mission)
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

            Waypoints = mission.Waypoints
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

    public async Task<MissionResponse?> UpdateAsync(
 Guid missionId,
 Guid userId,
 CreateMissionRequest request,
 CancellationToken cancellationToken = default)
    {
        Mission? mission =
            await _missionRepository.GetByIdForUpdateAsync(
                missionId,
                cancellationToken);

        if (mission is null)
        {
            return null;
        }

        MissionBusinessValidator.ValidateMissionOwnership(
    mission,
    userId);

        await _businessValidator.ValidateUavAsync(
            request.UavId,
            userId,
            cancellationToken);

        mission.UAVId = request.UavId;
        mission.Name = request.Name.Trim();

        mission.Description =
            string.IsNullOrWhiteSpace(request.Description)
                ? null
                : request.Description.Trim();

        mission.StartTime =
            request.StartTime.ToUniversalTime();

        mission.EndTime =
            request.EndTime.ToUniversalTime();

        await _missionRepository.DeleteWaypointsByMissionIdAsync(
            mission.Id,
            cancellationToken);

        DateTime now = DateTime.UtcNow;

        List<Waypoint> newWaypoints =
            request.Waypoints
                .OrderBy(x => x.SequenceOrder)
                .Select(waypointRequest => new Waypoint
                {
                    Id = Guid.NewGuid(),
                    MissionId = mission.Id,
                    SequenceOrder = waypointRequest.SequenceOrder,
                    Latitude = waypointRequest.Latitude,
                    Longitude = waypointRequest.Longitude,
                    Altitude = waypointRequest.Altitude,
                    ActionType = string.IsNullOrWhiteSpace(
                        waypointRequest.ActionType)
                            ? null
                            : waypointRequest.ActionType.Trim(),
                    CreatedAt = now
                })
                .ToList();

        await _missionRepository.AddWaypointsAsync(
            newWaypoints,
            cancellationToken);

        await _missionRepository.SaveChangesAsync(
            cancellationToken);

        mission.Waypoints = newWaypoints;

        return MapToResponse(mission);
    }
}