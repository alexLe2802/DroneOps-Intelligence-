using System.Net;
using DroneOps.Application.DTOs.Response.UAVs;
using DroneOps.Application.Interfaces.Storage;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;
using Microsoft.Extensions.Logging;

namespace DroneOps.Application.Services.UAVs;

/// <summary>
/// Owns optional UAV image storage lifecycle (direct publish, signed read, owner/manager delete, manager removal).
/// Image actions remain independent from UAV operational approval and assigned missions.
/// </summary>
internal sealed class UavImageWorkflow(
    IUavRepository uavRepository,
    IUserMediaStorage storage,
    UavNotificationHelper notifications,
    ILogger logger)
{
    public async Task<UavResponse> UploadOrReplaceAsync(
        Guid uavId,
        Guid actorUserId,
        string actorRole,
        Stream fileStream,
        string fileName,
        string contentType,
        long fileSize,
        CancellationToken cancellationToken = default)
    {
        var uav = await uavRepository.GetDetailAsync(uavId, cancellationToken)
            ?? throw new KeyNotFoundException("UAV not found.");

        EnsurePermission(uav, actorUserId, actorRole);

        var normalizedContentType = contentType.Trim().ToLowerInvariant();
        var imageBytes = await storage.ReadValidatedUavImageAsync(
            fileStream,
            fileName,
            normalizedContentType,
            fileSize,
            cancellationToken);

        var extension = normalizedContentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => throw new ArgumentException("Unsupported UAV image type.")
        };

        var newPath = $"uavs/{uav.Id:N}/{Guid.NewGuid():N}{extension}";

        await using (var uploadStream = new MemoryStream(imageBytes, writable: false))
        {
            await storage.UploadAsync(newPath, uploadStream, normalizedContentType, cancellationToken);
        }

        var oldPath = uav.ImageStoragePath;

        // UAV images are optional media and are published immediately. Managers may
        // remove inappropriate content later without changing operational approval.
        uav.ImageStoragePath = newPath;
        uav.ImageContentType = normalizedContentType;
        uav.ImageUpdatedAt = DateTimeOffset.UtcNow;
        uav.ImageModerationStatus = null;
        uav.ImageModerationAttempts = 0;
        uav.ImageModerationNextAttemptAt = null;
        uav.ImageModeratedAt = null;
        uav.ImageModerationReason = null;

        try
        {
            await uavRepository.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await CleanupAsync(newPath);
            throw;
        }

        if (!string.IsNullOrWhiteSpace(oldPath) &&
            !string.Equals(oldPath, newPath, StringComparison.Ordinal))
        {
            await CleanupAsync(oldPath);
        }

        return UavMapper.ToResponse(uav, uav.Pilot?.User?.FullName ?? string.Empty);
    }

    public async Task<(string Url, string ContentType)?> GetAsync(
        Guid uavId,
        CancellationToken cancellationToken = default)
    {
        var uav = await uavRepository.GetDetailAsync(uavId, cancellationToken);
        if (uav is null || string.IsNullOrWhiteSpace(uav.ImageStoragePath))
        {
            return null;
        }

        try
        {
            return (
                await storage.SignAsync(uav.ImageStoragePath, cancellationToken),
                uav.ImageContentType ?? "application/octet-stream");
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning("UAV image for {UavId} at path {Path} not found in storage.", uavId, uav.ImageStoragePath);
            return null;
        }
    }

    public async Task<bool> DeleteAsync(
        Guid uavId,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken = default)
    {
        var uav = await uavRepository.GetDetailAsync(uavId, cancellationToken);
        if (uav is null)
        {
            return false;
        }

        EnsurePermission(uav, actorUserId, actorRole);

        var oldPath = uav.ImageStoragePath;
        if (string.IsNullOrWhiteSpace(oldPath))
        {
            return true;
        }

        uav.ImageStoragePath = null;
        uav.ImageContentType = null;
        uav.ImageUpdatedAt = DateTimeOffset.UtcNow;
        uav.ImageModerationStatus = null;
        uav.ImageModerationAttempts = 0;
        uav.ImageModerationNextAttemptAt = null;
        uav.ImageModeratedAt = null;
        uav.ImageModerationReason = null;

        await uavRepository.SaveChangesAsync(cancellationToken);
        await CleanupAsync(oldPath);

        return true;
    }

    public async Task<UavResponse?> RejectAsync(
        Guid uavId,
        Guid managerUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var normalizedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason))
        {
            throw new ArgumentException("A rejection reason is required.");
        }

        if (normalizedReason.Length > 500)
        {
            throw new ArgumentException("The rejection reason must be 500 characters or fewer.");
        }

        var uav = await uavRepository.GetDetailAsync(uavId, cancellationToken);
        if (uav is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(uav.ImageStoragePath))
        {
            throw new InvalidOperationException("This UAV has no image to remove.");
        }

        var rejectedPath = uav.ImageStoragePath;
        uav.ImageStoragePath = null;
        uav.ImageContentType = null;
        uav.ImageUpdatedAt = DateTimeOffset.UtcNow;
        uav.ImageModerationStatus = "Rejected";
        uav.ImageModerationAttempts = 0;
        uav.ImageModerationNextAttemptAt = null;
        uav.ImageModeratedAt = DateTimeOffset.UtcNow;
        uav.ImageModerationReason = normalizedReason;

        await uavRepository.SaveChangesAsync(cancellationToken);
        await CleanupAsync(rejectedPath);
        await notifications.TryPublishImageRejectedAsync(uav, managerUserId, normalizedReason, cancellationToken);

        return UavMapper.ToResponse(uav, uav.Pilot?.User?.FullName ?? string.Empty);
    }

    public async Task CleanupAsync(string path)
    {
        try
        {
            await storage.DeleteAsync([path], CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to remove replaced UAV image {ImagePath}.", path);
        }
    }

    private static void EnsurePermission(UAV uav, Guid actorUserId, string actorRole)
    {
        var normalizedRole = actorRole.Trim().ToLowerInvariant();
        var isManager = normalizedRole is "operation_manager" or "operations_manager" or
            "operationsmanager" or "admin" or "systemadmin";
        if (!isManager && uav.Pilot?.UserId != actorUserId)
        {
            throw new UnauthorizedAccessException("You may only manage the image of your own UAV.");
        }
    }
}
