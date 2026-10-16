using DroneOps.Application.Interfaces.Missions;
using DroneOps.Domain.Entities;

namespace DroneOps.Application.Validators.Missions;

public sealed class MissionBusinessValidator
{
    private readonly IMissionRepository _missionRepository;

    public MissionBusinessValidator(
        IMissionRepository missionRepository)
    {
        _missionRepository = missionRepository;
    }

    public static void ValidateMissionOwnership(
        Mission mission,
        Guid userId)
    {
        if (mission.CreatedBy != userId)
        {
            throw new UnauthorizedAccessException(
                "You can only edit your own mission.");
        }
    }

    public async Task ValidateUavAsync(
        Guid uavId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        bool uavExists =
            await _missionRepository.UavExistsAsync(
                uavId,
                cancellationToken);

        if (!uavExists)
        {
            throw new KeyNotFoundException(
                "UAV was not found.");
        }

        bool isUavOwnedByUser =
            await _missionRepository.IsUavOwnedByUserAsync(
                uavId,
                userId,
                cancellationToken);

        if (!isUavOwnedByUser)
        {
            throw new UnauthorizedAccessException(
                "You can only assign your own UAV to the mission.");
        }
    }
}