using DroneOps.Application.Interfaces.Services;
using DroneOps.Application.Interfaces.Storage;
using DroneOps.Persistence.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DroneOps.Application.Services;

public sealed class AvatarService : IAvatarService
{
    private readonly IUserRepository _userRepository;
    private readonly IUserMediaStorage _storage;
    private readonly ILogger<AvatarService> _logger;

    public AvatarService(
        IUserRepository userRepository,
        IUserMediaStorage storage,
        ILogger<AvatarService> logger)
    {
        _userRepository = userRepository;
        _storage = storage;
        _logger = logger;
    }

    public async Task<string?> GetAvatarUrlAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(
            userId,
            cancellationToken);

        if (user?.AvatarPath is null)
        {
            return null;
        }

        return await _storage.SignAsync(
            user.AvatarPath,
            cancellationToken);
    }

    public async Task<string?> UploadAvatarAsync(
        Guid userId,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return null;
        }

        await _storage.ValidateAsync(
            file,
            true,
            cancellationToken);

        string newPath =
            $"avatars/{userId}/{Guid.NewGuid():N}";

        await _storage.UploadAsync(
            newPath,
            file,
            cancellationToken);

        string? oldPath = user.AvatarPath;

        user.AvatarPath = newPath;

        try
        {
            await _userRepository.SaveChangesAsync(
                cancellationToken);
        }
        catch
        {
            await CleanupAsync(newPath);
            throw;
        }

        if (oldPath is not null)
        {
            await CleanupAsync(oldPath);
        }

        return "/api/users/avatar?v=" +
               Uri.EscapeDataString(newPath);
    }

    private async Task CleanupAsync(string path)
    {
        try
        {
            await _storage.DeleteAsync(
                [path],
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Unable to remove replaced avatar.");
        }
    }
}