using DroneOps.Application.DTOs.Request.UAVs;
using DroneOps.Application.DTOs.Response.UAVs;
using DroneOps.Application.Interfaces.UAVs;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;

namespace DroneOps.Application.Services.UAVs;

public sealed class UavService
    : IUavService
{
    private readonly IUavRepository _uavRepository;
    private readonly IPilotRepository _pilotRepository;

    public UavService(
        IUavRepository uavRepository,
        IPilotRepository pilotRepository)
    {
        _uavRepository = uavRepository;
        _pilotRepository = pilotRepository;
    }

    public async Task<UavResponse> CreateAsync(
        Guid userId,
        CreateUavRequest request,
        CancellationToken cancellationToken = default)
    {
        var pilot = await _pilotRepository
            .GetByUserIdAsync(
                userId,
                cancellationToken);

        if (pilot is null)
        {
            throw new InvalidOperationException(
                "Pilot profile not found.");
        }

        var codeExists = await _uavRepository
            .CodeExistsAsync(
                request.Code,
                cancellationToken);

        if (codeExists)
        {
            throw new InvalidOperationException(
                "UAV code already exists.");
        }

        var uav = new UAV
        {
            Id = Guid.NewGuid(),

            PilotId = pilot.Id,

            Code = request.Code.Trim(),

            Name = request.Name.Trim(),

            Model = request.Model?.Trim(),

            Status = "Pending",

            CreatedAt = DateTime.UtcNow
        };

        await _uavRepository.AddAsync(
            uav,
            cancellationToken);

        await _uavRepository.SaveChangesAsync(
            cancellationToken);

        return MapToResponse(
            uav,
            pilot.User.FullName);
    }

    public async Task<List<UavResponse>> GetMyUavsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var pilot = await _pilotRepository
            .GetByUserIdAsync(
                userId,
                cancellationToken);

        if (pilot is null)
        {
            return [];
        }

        var uavs =
            await _uavRepository.GetByPilotIdAsync(
                pilot.Id,
                cancellationToken);

        return uavs
            .Select(x =>
                MapToResponse(
                    x,
                    pilot.User.FullName))
            .ToList();
    }

    public async Task<List<UavResponse>> GetPendingAsync(
        CancellationToken cancellationToken = default)
    {
        var uavs =
            await _uavRepository.GetPendingAsync(
                cancellationToken);

        return uavs
            .Select(x =>
                MapToResponse(
                    x,
                    x.Pilot.User.FullName))
            .ToList();
    }

    public async Task<UavResponse?> GetDetailAsync(
        Guid uavId,
        CancellationToken cancellationToken = default)
    {
        var uav =
            await _uavRepository.GetDetailAsync(
                uavId,
                cancellationToken);

        if (uav is null)
        {
            return null;
        }

        return MapToResponse(
            uav,
            uav.Pilot.User.FullName);
    }

    public async Task<UavResponse?> ApproveAsync(
        Guid uavId,
        CancellationToken cancellationToken = default)
    {
        var uav =
            await _uavRepository.GetDetailAsync(
                uavId,
                cancellationToken);

        if (uav is null)
        {
            return null;
        }

        uav.Status = "Active";

        await _uavRepository.SaveChangesAsync(
            cancellationToken);

        return MapToResponse(
            uav,
            uav.Pilot.User.FullName);
    }
    public async Task<UavResponse?> RejectAsync(
    Guid uavId,
    CancellationToken cancellationToken = default)
    {
        var uav =
            await _uavRepository.GetDetailAsync(
                uavId,
                cancellationToken);

        if (uav is null)
        {
            return null;
        }

        uav.Status = "Rejected";

        await _uavRepository.SaveChangesAsync(
            cancellationToken);

        return MapToResponse(
            uav,
            uav.Pilot.User.FullName);
    }
    public async Task<UavResponse?> DeactivateAsync(
    Guid uavId,
    CancellationToken cancellationToken = default)
    {
        var uav =
            await _uavRepository.GetDetailAsync(
                uavId,
                cancellationToken);

        if (uav is null)
        {
            return null;
        }

        uav.Status = "Inactive";

        await _uavRepository.SaveChangesAsync(
            cancellationToken);

        return MapToResponse(
            uav,
            uav.Pilot.User.FullName);
    }
    public async Task<UavResponse?> ActivateAsync(
    Guid uavId,
    CancellationToken cancellationToken = default)
    {
        var uav =
            await _uavRepository.GetDetailAsync(
                uavId,
                cancellationToken);

        if (uav is null)
        {
            return null;
        }

        uav.Status = "Active";

        await _uavRepository.SaveChangesAsync(
            cancellationToken);

        return MapToResponse(
            uav,
            uav.Pilot.User.FullName);
    }

    private static UavResponse MapToResponse(
     UAV uav,
     string pilotName)
    {
        return new UavResponse
        {
            Id = uav.Id,
            PilotId = uav.PilotId,
            PilotName = pilotName,
            Code = uav.Code,
            Name = uav.Name,
            Model = uav.Model,
            Status = uav.Status,
            CreatedAt = uav.CreatedAt
        };
    }
}

   