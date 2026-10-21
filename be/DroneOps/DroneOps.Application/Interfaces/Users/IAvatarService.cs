using Microsoft.AspNetCore.Http;

namespace DroneOps.Application.Interfaces.Services;

public interface IAvatarService
{
    Task<string?> GetAvatarUrlAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<string?> UploadAvatarAsync(
        Guid userId,
        IFormFile file,
        CancellationToken cancellationToken = default);
}