using DroneOps.Application.DTOs.Response.Users;
using DroneOps.Application.Interfaces.Storage;
using DroneOps.Application.Interfaces.Users;
using DroneOps.Domain;
using DroneOps.Persistence.Interfaces;

namespace DroneOps.Application.Services.Users;

public sealed class OperatorCredentialsService
    : IOperatorCredentialsService
{
    private readonly IUserRepository _userRepository;
    private readonly IPilotRepository _pilotRepository;
    private readonly IUserMediaStorage _storage;

    public OperatorCredentialsService(
        IUserRepository userRepository,
        IPilotRepository pilotRepository,
        IUserMediaStorage storage)
    {
        _userRepository = userRepository;
        _pilotRepository = pilotRepository;
        _storage = storage;
    }

    public async Task<string?> GetAvatarAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user =
            await _userRepository.GetByIdWithRoleAsync(
                userId,
                cancellationToken);

        if (user is null)
        {
            return null;
        }

        if (CanonicalRoles.Normalize(user.Role.Name)
            != "uav_operator")
        {
            return null;
        }

        if (user.AvatarPath is null)
        {
            return null;
        }

        return await _storage.SignAsync(
            user.AvatarPath,
            cancellationToken);
    }

    public async Task<OperatorCredentialsResponse?> GetCredentialsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var pilot =
            await _pilotRepository.GetByUserIdWithRoleAsync(
                userId,
                cancellationToken);

        if (pilot is null)
        {
            return null;
        }

        if (CanonicalRoles.Normalize(
                pilot.User.Role.Name)
            != "uav_operator")
        {
            return null;
        }

        var licenseImages =
            await _pilotRepository
                .GetCurrentPilotLicenseImagesAsync(
                    pilot.Id,
                    cancellationToken);

        if (licenseImages.Count == 0)
        {
            licenseImages =
                await _pilotRepository
                    .GetLegacyPilotLicenseImagesAsync(
                        pilot.User.Email,
                        pilot.PilotLicenseNo,
                        cancellationToken);
        }

        var urls = new List<string>();
        bool documentsAvailable = true;

        foreach (var image in licenseImages)
        {
            string path = image.ImageUrl;

            if (!path.StartsWith("licenses/"))
            {
                continue;
            }

            try
            {
                string signedUrl =
                    await _storage.SignAsync(
                        path,
                        cancellationToken);

                urls.Add(signedUrl);
            }
            catch (Exception exception)
                when (
                    exception is HttpRequestException ||
                    exception is InvalidOperationException ||
                    exception is ArgumentException)
            {
                documentsAvailable = false;
            }
        }

        return new OperatorCredentialsResponse
        {
            PhoneNumber = pilot.PhoneNumber,
            DateOfBirth = pilot.DateOfBirth,
            ExperienceYears = pilot.ExperienceYears,
            PilotLicenseNo = pilot.PilotLicenseNo,
            LicenseIssuedDate = pilot.LicenseIssuedDate,
            LicenseExpiredDate = pilot.LicenseExpiredDate,
            LicenseImageUrls = urls,
            DocumentsAvailable = documentsAvailable
        };
    }
}