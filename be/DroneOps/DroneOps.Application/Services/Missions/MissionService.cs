using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Interfaces.PostFlight;
using DroneOps.Domain.Entities;
using DroneOps.Application.Services.Missions;
using DroneOps.Application.Interfaces.Notifications;
using Microsoft.Extensions.Logging;

namespace DroneOps.Application.Services;

public sealed class MissionService : IMissionService
{
    private readonly IMissionRepository _missionRepository;
    private readonly MissionWorkflowService _workflow;
    private readonly MissionValidationService _validation;
    private readonly IPostFlightService _postFlightService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<MissionService> _logger;

    public MissionService(
       IMissionRepository missionRepository,
       MissionWorkflowService workflow,
       MissionValidationService validation,
       IPostFlightService postFlightService,
       INotificationService notificationService,
       ILogger<MissionService> logger)
    {
        _missionRepository = missionRepository;
        _workflow = workflow;
        _validation = validation;
        _postFlightService = postFlightService;
        _notificationService = notificationService;
        _logger = logger;
    }
    public async Task<MissionResponse> CreateAsync(
        Guid pilotId,
        CreateMissionRequest request,
        CancellationToken cancellationToken = default)
    {
        var mission = await _workflow.SavePlanAsync(pilotId, request, null, cancellationToken);
        await TryPublishPendingNotificationAsync(mission, pilotId, cancellationToken);
        return await MapWithValidationAsync(mission, cancellationToken);
    }

    public async Task<MissionResponse?> GetByIdAsync(
        Guid missionId,
        CancellationToken cancellationToken = default)
    {
        await _postFlightService.SynchronizeOverdueMissionsAsync(cancellationToken);
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

    public async Task<MissionPagedResponse> GetAllAsync(
     MissionFilterRequest request,
     CancellationToken cancellationToken = default)
    {
        await _postFlightService.SynchronizeOverdueMissionsAsync(
            cancellationToken);

        if (request.PageNumber < 1)
            request.PageNumber = 1;

        if (request.PageSize < 1)
            request.PageSize = 10;

        var (missions, totalCount) =
            await _missionRepository.GetAllAsync(
                request.Search,
                request.Status,
                request.FromDate,
                request.ToDate,
                request.PageNumber,
                request.PageSize,
                cancellationToken);

        var results = new List<MissionResponse>();

        foreach (var mission in missions)
        {
            results.Add(
                await MapWithValidationAsync(
                    mission,
                    cancellationToken));
        }

        return new MissionPagedResponse
        {
            Items = results,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(
                totalCount / (double)request.PageSize)
        };
    }

    public async Task<List<MissionResponse>> GetMyAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await _postFlightService.SynchronizeOverdueMissionsAsync(cancellationToken);
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
        response.LatestValidation = await _validation.ReadLatestRunAsync(mission.Id,mission.CreatedBy,false,ct);
        response.PendingUpdate = await _validation.PendingAsync(mission.Id, ct);
        var preflight = await _validation.LatestPreflightAsync(mission.Id, ct);
        response.PreflightValidation = preflight;
        response.FlightReadiness = preflight?.Status ?? "AwaitingPreflight";
        return response;
    }

    public async Task<MissionResponse?> UpdateAsync(Guid id, Guid userId, CreateMissionRequest request, CancellationToken cancellationToken = default)
    {
        var mission = await _workflow.SavePlanAsync(userId, request, id, cancellationToken);
        await TryPublishPendingNotificationAsync(mission, userId, cancellationToken);
        return await MapWithValidationAsync(mission, cancellationToken);
    }

    private async Task TryPublishPendingNotificationAsync(Mission mission, Guid actorUserId, CancellationToken cancellationToken)
    {
        try
        {
            await _notificationService.PublishPendingMissionAsync(
                mission.Id, actorUserId, mission.Name, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not publish pending-mission notification for {MissionId}.", mission.Id);
        }
    }
}
