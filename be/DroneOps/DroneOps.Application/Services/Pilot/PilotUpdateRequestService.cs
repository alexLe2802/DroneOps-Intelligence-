using System.Text.Json;
using DroneOps.Application.DTOs.Request.Users;
using DroneOps.Application.Interfaces.Storage;
using DroneOps.Application.Interfaces.Users;
using DroneOps.Persistence.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Http;

namespace DroneOps.Application.Services.Users;

public sealed class PilotUpdateRequestService
    : IPilotUpdateRequestService
{
    private readonly IPilotRepository _pilotRepository;
    private readonly IUserService _userService;
    private readonly IUserMediaStorage _storage;

    private readonly IValidator<UpdatePilotProfileRequest>
        _validator;

    public PilotUpdateRequestService(
        IPilotRepository pilotRepository,
        IUserService userService,
        IUserMediaStorage storage,
        IValidator<UpdatePilotProfileRequest> validator)
    {
        _pilotRepository = pilotRepository;
        _userService = userService;
        _storage = storage;
        _validator = validator;
    }

    public async Task<object> GetAllAsync(
        Guid userId,
        bool isManager,
        PilotUpdateRequestFilterRequest filter,
        CancellationToken cancellationToken = default)
    {
        int pageNumber = Math.Max(
            1,
            filter.PageNumber);

        int pageSize = Math.Clamp(
            filter.PageSize,
            1,
            100);

        var (items, totalCount) =
            await _pilotRepository.GetAllPilotRequestsAsync(
                userId,
                isManager,
                pageNumber,
                pageSize,
                filter.Search,
                filter.Status,
                cancellationToken);

        return new
        {
            items = items.Select(x => new
            {
                x.Id,
                x.PilotId,

                userId = x.Pilot.UserId,
                fullName = x.Pilot.User.FullName,
                email = x.Pilot.User.Email,

                x.PilotLicenseNo,
                x.LicenseIssuedDate,
                x.LicenseExpiredDate,

                x.Status,
                x.RejectReason,
                x.CreatedAt,
                x.ReviewedAt,
                x.ReviewedBy
            }),

            pageNumber,
            pageSize,

            totalItems = totalCount,

            totalPages = (int)Math.Ceiling(
                totalCount / (double)pageSize)
        };
    }

    public async Task<object?> GetByIdAsync(
        Guid requestId,
        Guid userId,
        bool isManager,
        CancellationToken cancellationToken = default)
    {
        var item =
            await _pilotRepository
                .GetUpdateRequestDetailAsync(
                    requestId,
                    userId,
                    isManager,
                    cancellationToken);

        if (item is null)
        {
            return null;
        }

        var licenseImageUrls = new List<string>();

        foreach (var image in item.LicenseImages)
        {
            if (string.IsNullOrWhiteSpace(
                    image.ImageUrl))
            {
                continue;
            }

            if (image.ImageUrl.StartsWith(
                    "licenses/",
                    StringComparison.OrdinalIgnoreCase))
            {
                string signedUrl =
                    await _storage.SignAsync(
                        image.ImageUrl,
                        cancellationToken);

                licenseImageUrls.Add(signedUrl);
            }
            else
            {
                licenseImageUrls.Add(
                    image.ImageUrl);
            }
        }

        return new
        {
            item.Id,
            item.PilotId,

            userId = item.Pilot.UserId,
            fullName = item.Pilot.User.FullName,
            email = item.Pilot.User.Email,

            item.CreatedAt,
            item.ReviewedAt,
            item.ReviewedBy,

            item.Status,
            item.RejectReason,

            item.PilotLicenseNo,
            item.LicenseIssuedDate,
            item.LicenseExpiredDate,

            currentLicenseNo =
                item.Pilot.PilotLicenseNo,

            currentIssuedDate =
                item.Pilot.LicenseIssuedDate,

            currentExpiredDate =
                item.Pilot.LicenseExpiredDate,

            licenseImageUrls
        };
    }

    public async Task<object> SubmitAsync(
     Guid userId,
     PilotUpdateRequestForm form,
     CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(form.PilotLicenseNo))
        {
            throw new ArgumentException(
                "Pilot license number is required.");
        }

        if (form.Files is null ||
            form.Files.Count is < 1 or > 10)
        {
            throw new ArgumentException(
                "Attach between 1 and 10 license documents.");
        }

        var request = new UpdatePilotProfileRequest
        {
            PilotLicenseNo = form.PilotLicenseNo.Trim(),
            LicenseIssuedDate = form.LicenseIssuedDate,
            LicenseExpiredDate = form.LicenseExpiredDate,

            PhoneNumber = null,
            DateOfBirth = null,
            ExperienceYears = null,

            // Giá trị tạm để chạy validator.
            LicenseImageUrls = ["upload-pending"]
        };

        var validationResult =
            await _validator.ValidateAsync(
                request,
                cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ArgumentException(
                validationResult.Errors[0].ErrorMessage);
        }

        foreach (var file in form.Files)
        {
            await _storage.ValidateAsync(
                file,
                false,
                cancellationToken);
        }

        var pilot =
            await _pilotRepository.GetByUserIdAsync(
                userId,
                cancellationToken);

        if (pilot is null)
        {
            throw new KeyNotFoundException(
                "Pilot profile not found.");
        }

        bool hasPendingRequest =
            await _pilotRepository
                .HasPendingUpdateRequestAsync(
                    pilot.Id,
                    cancellationToken);

        if (hasPendingRequest)
        {
            throw new InvalidOperationException(
                "You already have a pending license change.");
        }

        var uploadedPaths = new List<string>();

        try
        {
            foreach (var file in form.Files)
            {
                string extension =
                    GetFileExtension(file);

                string path =
                    $"licenses/{userId:N}/" +
                    $"{Guid.NewGuid():N}{extension}";

                await _storage.UploadAsync(
                    path,
                    file,
                    cancellationToken);

                uploadedPaths.Add(path);
            }

            request.LicenseImageUrls =
                uploadedPaths;

            return await _userService
                .UpdatePilotProfileAsync(
                    userId,
                    request,
                    cancellationToken);
        }
        catch
        {
            if (uploadedPaths.Count > 0)
            {
                try
                {
                    await _storage.DeleteAsync(
                        uploadedPaths,
                        CancellationToken.None);
                }
                catch
                {
                }
            }

            throw;
        }
    }

    public async Task<object> ApproveAsync(
        Guid requestId,
        Guid reviewedBy,
        CancellationToken cancellationToken = default)
    {
        var item =
            await _pilotRepository
                .GetUpdateRequestForReviewAsync(
                    requestId,
                    cancellationToken);

        if (item is null)
        {
            throw new KeyNotFoundException(
                "Pilot update request not found.");
        }

        if (!string.Equals(
                item.Status,
                "Pending",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Request was already reviewed. " +
                "Refresh the list.");
        }

        if (!item.LicenseExpiredDate.HasValue)
        {
            throw new ArgumentException(
                "License expiration date is required.");
        }

        DateOnly currentDate =
            DateOnly.FromDateTime(
                DateTime.UtcNow);

        if (item.LicenseExpiredDate.Value <
            currentDate)
        {
            throw new ArgumentException(
                "This license has expired " +
                "and cannot be approved.");
        }

        // Giữ nguyên nghiệp vụ approve cũ:
        // - cập nhật Pilot
        // - xử lý ảnh cũ
        // - gắn ảnh mới
        // - cập nhật trạng thái request
        return await _userService
            .ApprovePilotUpdateRequestAsync(
                requestId,
                reviewedBy,
                cancellationToken);
    }

    public async Task<object> RejectAsync(
        Guid requestId,
        Guid reviewedBy,
        RejectPilotUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(
                request.RejectReason))
        {
            throw new ArgumentException(
                "Enter a rejection reason.");
        }

        string rejectReason =
            request.RejectReason.Trim();

        if (rejectReason.Length > 1000)
        {
            throw new ArgumentException(
                "Enter a rejection reason " +
                "up to 1000 characters.");
        }

        var item =
            await _pilotRepository
                .GetUpdateRequestForReviewAsync(
                    requestId,
                    cancellationToken);

        if (item is null)
        {
            throw new KeyNotFoundException(
                "Pilot update request not found.");
        }

        if (!string.Equals(
                item.Status,
                "Pending",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Request was already reviewed. " +
                "Refresh the list.");
        }

        request.RejectReason = rejectReason;

        // Giữ nguyên nghiệp vụ reject cũ.
        return await _userService
            .RejectPilotUpdateRequestAsync(
                requestId,
                reviewedBy,
                request,
                cancellationToken);
    }

    private static string GetFileExtension(
        IFormFile file)
    {
        return file.ContentType
            .ToLowerInvariant() switch
        {
            "application/pdf" => ".pdf",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/jpeg" => ".jpg",
            "image/jpg" => ".jpg",

            _ => Path
                .GetExtension(file.FileName)
                .ToLowerInvariant()
        };
    }
}