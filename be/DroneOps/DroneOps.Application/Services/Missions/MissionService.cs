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

    public async Task<List<MissionResponse>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        List<Mission> missions =
            await _missionRepository.GetAllAsync(
                cancellationToken);

        return missions
            .Select(MapToResponse)
            .ToList();
    }

    public async Task<List<MissionResponse>> GetMyAsync(
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

    public async Task<bool> SubmitMissionAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var mission = await _missionRepository.GetByIdAsync(id, cancellationToken);
        if (mission == null || mission.Status != "Draft") return false;

        mission.Status = "Submitted";

        await _missionRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<object> ValidateMissionParamsAsync(
        MissionValidateDto request,
        CancellationToken cancellationToken = default)
    {
        bool isValid = request.Waypoints != null && request.Waypoints.Count > 0;
        return await Task.FromResult(new
        {
            isValid = isValid,
            message = isValid ? "Mission parameters are valid." : "Waypoints cannot be empty."
        });
    }

    public async Task<bool> CancelMissionAsync(
        Guid id,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var mission = await _missionRepository.GetByIdAsync(id, cancellationToken);
        if (mission == null) return false;

        mission.Status = "Cancelled";

        await _missionRepository.SaveChangesAsync(cancellationToken);
        return true;
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
}