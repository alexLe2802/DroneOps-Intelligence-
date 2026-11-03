using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces;
using DroneOps.Application.Interfaces.Geofences;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Interfaces.Notifications;
using DroneOps.Application.Interfaces.PostFlight;
using DroneOps.Application.Interfaces.Validators;
using DroneOps.Application.Services.Missions;
using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace DroneOps.Application.Services;

public sealed class MissionService : IMissionService
{
    private readonly IMissionRepository _missionRepository;
    private readonly IMissionWorkflowService _workflow;
    private readonly IMissionValidationService _validation;
    private readonly IPostFlightService _postFlightService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<MissionService> _logger;
    private readonly IGeofenceService _geofenceService;

    public MissionService(
      IMissionRepository missionRepository,
      IMissionWorkflowService workflow,
      IMissionValidationService validation,
      IPostFlightService postFlightService,
      INotificationService notificationService,
      IGeofenceService geofenceService,
      ILogger<MissionService> logger)
    {
        _missionRepository = missionRepository;
        _workflow = workflow;
        _validation = validation;
        _postFlightService = postFlightService;
        _notificationService = notificationService;
        _geofenceService = geofenceService;
        _logger = logger;
    }
    public async Task<MissionResponse> CreateAsync(
    Guid pilotId,
    CreateMissionRequest request,
    CancellationToken cancellationToken = default)
    {
        // Existing flow
        var mission = await _workflow.SavePlanAsync(
            pilotId,
            request,
            null,
            cancellationToken);

        if (mission.Status == "Pending Review")
        {
            await TryPublishPendingNotificationAsync(
                mission,
                pilotId,
                cancellationToken);
        }

        return await MapWithValidationAsync(
            mission,
            cancellationToken);
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

    public async Task<MissionPagedResponse> GetAllAsync(
     MissionFilterRequest request,
     CancellationToken cancellationToken = default)
    {
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

        var results = await MapManyWithValidationAsync(
            missions,
            missions.Select(mission => mission.CreatedBy).FirstOrDefault(),
            true,
            cancellationToken);

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
        List<Mission> missions =
            await _missionRepository.GetByCreatorAsync(
                userId,
                cancellationToken);

        return await MapManyWithValidationAsync(
            missions,
            userId,
            false,
            cancellationToken);
    }

    private async Task<List<MissionResponse>> MapManyWithValidationAsync(
        IReadOnlyCollection<Mission> missions,
        Guid actor,
        bool manager,
        CancellationToken cancellationToken)
    {
        var snapshots = await _validation.ReadManyAsync(
            missions,
            actor,
            manager,
            cancellationToken);

        return missions.Select(mission =>
        {
            var response = MapToResponse(mission);
            if (!snapshots.TryGetValue(mission.Id, out var snapshot))
            {
                return response;
            }

            response.Validation = snapshot.Validation;
            response.LatestValidation = snapshot.LatestValidation;
            response.PreflightValidation = snapshot.PreflightValidation;
            response.FlightReadiness = snapshot.PreflightValidation?.Status ?? "AwaitingPreflight";
            return response;
        }).ToList();
    }

    private static MissionResponse MapToResponse(
        Mission mission)
    {
        return new MissionResponse
        {
            Id = mission.Id,
            CreatedBy = mission.CreatedBy,
            UavId = mission.UAVId,
            UavCode = mission.UAV?.Code,
            Name = mission.Name,
            AltitudeReference=mission.AltitudeReference,
            Description = mission.Description,
            Status = mission.Status,
            StartTime = mission.StartTime,
            EndTime = mission.EndTime,

            AverageSpeedMetersPerSecond =
    mission.AverageSpeedMetersPerSecond,

            MaxSpeedMetersPerSecond =
    mission.MaxSpeedMetersPerSecond,

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

    private async Task<MissionResponse> MapWithValidationAsync(
     Mission mission,
     CancellationToken ct)
    {
        var response = MapToResponse(mission);

        response.Validation = await _validation.ReadAsync(
            mission.Id,
            mission.CreatedBy,
            false,
            ct);

        response.LatestValidation = await _validation.ReadLatestRunAsync(
            mission.Id,
            mission.CreatedBy,
            false,
            ct);

        var preflight = await _validation.LatestPreflightAsync(
            mission.Id,
            ct);

        response.PreflightValidation = preflight;
        response.FlightReadiness = preflight?.Status ?? "AwaitingPreflight";

        return response;
    }
    public async Task<MissionResponse?> UpdateAsync(Guid id, Guid userId, CreateMissionRequest request, CancellationToken cancellationToken = default)
    {
        var mission = await _workflow.SavePlanAsync(userId, request, id, cancellationToken);
        if (mission.Status == "Pending Review")
        {
            await TryPublishPendingNotificationAsync(mission, userId, cancellationToken);
        }
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
