using DroneOps.Application.DTOs.Request.Users;
using DroneOps.Application.DTOs.Response.Common;
using DroneOps.Application.DTOs.Response.Users;
using DroneOps.Application.Interfaces.Users;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;

namespace DroneOps.Application.Services.Users;

public sealed class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IPilotRepository _pilotRepository;

    public UserService(
        IUserRepository userRepository,
        IPilotRepository pilotRepository)
    {
        _userRepository = userRepository;
        _pilotRepository = pilotRepository;
    }

    public async Task<UserProfileResponse?> GetProfileAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        User? user =
            await _userRepository.GetByIdWithRoleAsync(
                userId,
                cancellationToken);

        if (user is null)
        {
            return null;
        }

        return MapToProfileResponse(user);
    }

    public async Task<UserProfileResponse?> UpdateProfileAsync(
        Guid userId,
        UpdateProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        User? user =
            await _userRepository.GetByIdAsync(
                userId,
                cancellationToken);

        if (user is null)
        {
            return null;
        }

        string fullName = request.FullName.Trim();

        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new ArgumentException(
                "Full name cannot be empty.");
        }

        user.FullName = fullName;

        bool saved =
            await _userRepository.SaveChangesAsync(
                cancellationToken);

        if (!saved)
        {
            throw new InvalidOperationException(
                "Profile was not updated.");
        }

        User? updatedUser =
            await _userRepository.GetByIdWithRoleAsync(
                userId,
                cancellationToken);

        if (updatedUser is null)
        {
            return null;
        }

        return MapToProfileResponse(updatedUser);
    }

    private static UserProfileResponse MapToProfileResponse(
        User user)
    {
        return new UserProfileResponse
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role.Name,
            CreatedAt = user.CreatedAt
        };
    }
    public async Task<PilotProfileResponse?>
     GetPilotProfileAsync(
         Guid userId,
         CancellationToken cancellationToken = default)
    {
        var pilot = await _pilotRepository
            .GetByUserIdAsync(
                userId,
                cancellationToken);

        if (pilot is null)
        {
            return null;
        }

        return new PilotProfileResponse
        {
            UserId = pilot.User.Id,

            PilotId = pilot.Id,

            FullName = pilot.User.FullName,

            Email = pilot.User.Email,

            PhoneNumber = pilot.PhoneNumber,

            DateOfBirth = pilot.DateOfBirth,

            PilotLicenseNo = pilot.PilotLicenseNo,

            LicenseIssuedDate = pilot.LicenseIssuedDate,

            LicenseExpiredDate = pilot.LicenseExpiredDate,

            ExperienceYears = pilot.ExperienceYears
        };
    }

    public async Task<PagedResponse<UserProfileResponse>>
      GetAllUsersAsync(
          UserFilterRequest request,
          CancellationToken cancellationToken = default)
    {
        int pageNumber =
            request.PageNumber < 1
                ? 1
                : request.PageNumber;

        int pageSize = request.PageSize switch
        {
            < 1 => 10,
            > 100 => 100,
            _ => request.PageSize
        };

        string? search =
            string.IsNullOrWhiteSpace(request.Search)
                ? null
                : request.Search.Trim();

        string? role =
            string.IsNullOrWhiteSpace(request.Role)
                ? null
                : request.Role.Trim();

        (
            IReadOnlyList<User> Items,
            int TotalCount
        ) result = await _userRepository.GetPagedWithRoleAsync(
            pageNumber,
            pageSize,
            search,
            role,
            cancellationToken);

        IReadOnlyList<UserProfileResponse> items =
            result.Items
                .Select(MapToProfileResponse)
                .ToList();

        int totalPages = result.TotalCount == 0
            ? 0
            : (int)Math.Ceiling(
                result.TotalCount / (double)pageSize);

        return new PagedResponse<UserProfileResponse>
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalItems = result.TotalCount,
            TotalPages = totalPages
        };
    }
}