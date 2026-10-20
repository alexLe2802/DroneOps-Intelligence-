using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Application.Services.Missions;

namespace DroneOps.Application.Services;

public sealed class MissionService : IMissionService
{
    private readonly IMissionRepository _missionRepository;
    private readonly MissionWorkflowService _workflow;
    private readonly MissionValidationService _validation;

    public MissionService(
       IMissionRepository missionRepository,
       MissionWorkflowService workflow, MissionValidationService validation)
    {
        _missionRepository = missionRepository;
        _workflow = workflow;
        _validation = validation;
    }
    public async Task<MissionResponse> CreateAsync(
        Guid pilotId,
        CreateMissionRequest request,
        CancellationToken cancellationToken = default)
    {
        var mission = await _workflow.SavePlanAsync(pilotId, request, null, cancellationToken);
        return await MapWithValidationAsync(mission, cancellationToken);
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

        return await MapWithValidationAsync(mission, cancellationToken);
    }

    public async Task<List<MissionResponse>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        List<Mission> missions =
            await _missionRepository.GetAllAsync(
                cancellationToken);

        var results = new List<MissionResponse>();
        foreach (var mission in missions) results.Add(await MapWithValidationAsync(mission, cancellationToken));
        return results;
    }

    public async Task<List<MissionResponse>> GetMyAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        List<Mission> missions =
            await _missionRepository.GetByCreatorAsync(
                userId,
                cancellationToken);

        var results = new List<MissionResponse>();
        foreach (var mission in missions) results.Add(await MapWithValidationAsync(mission, cancellationToken));
        return results;
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
            AltitudeReference=mission.AltitudeReference,
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

    private async Task<MissionResponse> MapWithValidationAsync(Mission mission, CancellationToken ct)
    {
        var response = MapToResponse(mission);
        response.Validation = await _validation.ReadAsync(mission.Id,mission.CreatedBy,false,null,ct);
        response.PendingUpdate = await _validation.PendingAsync(mission.Id, ct);
        var preflight = await _validation.LatestPreflightAsync(mission.Id, ct);
        response.PreflightValidation = preflight;
        response.FlightReadiness = preflight?.Status ?? "AwaitingPreflight";
        return response;
    }

    public async Task<MissionResponse?> UpdateAsync(Guid id, Guid userId, CreateMissionRequest request, CancellationToken cancellationToken = default)
    {
        var mission = await _workflow.SavePlanAsync(userId, request, id, cancellationToken);
        return await MapWithValidationAsync(mission, cancellationToken);
    }
}
