using DroneOps.Application.DTOs.Users;
using DroneOps.Application.Interfaces.Users;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;

namespace DroneOps.Application.Services.Users;

public sealed class UserService : IUserService
{
    private readonly IUserRepository _userRepository;

    public UserService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
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
}