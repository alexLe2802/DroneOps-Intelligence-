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

        await _pilotRepository.SaveChangesAsync(
      cancellationToken);

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

    public async Task<UpdatePilotProfileResponse?>
    UpdatePilotProfileAsync(
        Guid userId,
        UpdatePilotProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        Pilot? pilot =
            await _pilotRepository.GetByUserIdForUpdateAsync(
                userId,
                cancellationToken);

        if (pilot is null)
        {
            return null;
        }

        bool hasLicenseUpdate =
            HasLicenseUpdate(request);

        // Kiểm tra request license đang Pending
        if (hasLicenseUpdate)
        {
            bool hasPendingRequest =
                await _pilotRepository
                    .HasPendingUpdateRequestAsync(
                        pilot.Id,
                        cancellationToken);

            if (hasPendingRequest)
            {
                throw new InvalidOperationException(
                    "You already have a pending pilot update request.");
            }
        }

        // Theo dõi xem thông tin thường có thực sự thay đổi không
        bool profileChanged = false;

        // Update PhoneNumber nếu FE có gửi
        if (request.PhoneNumber is not null)
        {
            string? newPhoneNumber =
                string.IsNullOrWhiteSpace(request.PhoneNumber)
                    ? null
                    : request.PhoneNumber.Trim();

            if (pilot.PhoneNumber != newPhoneNumber)
            {
                pilot.PhoneNumber = newPhoneNumber;
                profileChanged = true;
            }
        }

        // Update DateOfBirth nếu FE có gửi và khác giá trị cũ
        if (request.DateOfBirth.HasValue &&
            pilot.DateOfBirth != request.DateOfBirth.Value)
        {
            pilot.DateOfBirth =
                request.DateOfBirth.Value;

            profileChanged = true;
        }

        // Update ExperienceYears nếu FE có gửi và khác giá trị cũ
        if (request.ExperienceYears.HasValue &&
            pilot.ExperienceYears != request.ExperienceYears.Value)
        {
            pilot.ExperienceYears =
                request.ExperienceYears.Value;

            profileChanged = true;
        }

        PilotUpdateRequest? updateRequest = null;

        // License không update trực tiếp vào Pilot
        // mà tạo request Pending
        if (hasLicenseUpdate)
        {
            Guid updateRequestId =
                Guid.NewGuid();

            DateTime createdAt =
                DateTime.UtcNow;

            updateRequest =
                new PilotUpdateRequest
                {
                    Id = updateRequestId,

                    PilotId = pilot.Id,

                    PilotLicenseNo =
                        request.PilotLicenseNo!.Trim(),

                    LicenseIssuedDate =
                        request.LicenseIssuedDate,

                    LicenseExpiredDate =
                        request.LicenseExpiredDate,

                    Status = "Pending",

                    RejectReason = null,

                    ReviewedBy = null,

                    ReviewedAt = null,

                    CreatedAt = createdAt,

                    LicenseImages =
                        request.LicenseImageUrls!
                            .Where(x =>
                                !string.IsNullOrWhiteSpace(x))
                            .Select(x => x.Trim())
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .Select(imageUrl =>
                                new LicenseImage
                                {
                                    Id = Guid.NewGuid(),

                                    ImageUrl = imageUrl,

                                    ImageType =
                                        "PilotLicense",

                                    CreatedAt =
                                        createdAt,

                                    // Không thuộc registration
                                    PilotRegistrationId =
                                        null,

                                    // Link tới request đang Pending
                                    PilotUpdateRequestId =
                                        updateRequestId,

                                    // Chưa được Admin duyệt
                                    PilotId =
                                        null
                                })
                            .ToList()
                };

            await _pilotRepository
                .AddUpdateRequestAsync(
                    updateRequest,
                    cancellationToken);
        }

        // Chỉ save khi thực sự có thay đổi
        // hoặc có PilotUpdateRequest mới cần insert
        if (profileChanged || hasLicenseUpdate)
        {
            await _pilotRepository.SaveChangesAsync(
                cancellationToken);
        }

        return new UpdatePilotProfileResponse
        {
            UserId = pilot.UserId,

            PilotId = pilot.Id,

            FullName = pilot.User.FullName,

            Email = pilot.User.Email,

            PhoneNumber = pilot.PhoneNumber,

            DateOfBirth = pilot.DateOfBirth,

            ExperienceYears =
                pilot.ExperienceYears,

            // License chính thức vẫn giữ nguyên
            // cho đến khi Admin approve
            PilotLicenseNo =
                pilot.PilotLicenseNo,

            LicenseIssuedDate =
                pilot.LicenseIssuedDate,

            LicenseExpiredDate =
                pilot.LicenseExpiredDate,

            PilotUpdateRequestId =
                updateRequest?.Id,

            PilotUpdateRequestStatus =
                updateRequest?.Status
        };
    }
    private static bool HasLicenseUpdate(
    UpdatePilotProfileRequest request)
    {
        return
            !string.IsNullOrWhiteSpace(
                request.PilotLicenseNo) ||
            request.LicenseIssuedDate.HasValue ||
            request.LicenseExpiredDate.HasValue ||
            request.LicenseImageUrls is { Count: > 0 };
    }
}