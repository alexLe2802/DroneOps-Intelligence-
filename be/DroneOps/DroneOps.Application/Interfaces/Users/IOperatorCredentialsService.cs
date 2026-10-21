using DroneOps.Application.DTOs.Response.Users;

namespace DroneOps.Application.Interfaces.Users;

public interface IOperatorCredentialsService
{
    Task<string?> GetAvatarAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<OperatorCredentialsResponse?> GetCredentialsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}