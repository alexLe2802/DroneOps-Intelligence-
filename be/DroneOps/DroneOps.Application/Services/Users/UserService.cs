using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DroneOps.Application.DTOs.Request.Users;
using DroneOps.Application.DTOs.Response.Common;
using DroneOps.Application.DTOs.Response.Users;
using DroneOps.Application.Interfaces.Auth;
using DroneOps.Application.Interfaces.Users;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using BC = BCrypt.Net.BCrypt;

namespace DroneOps.Application.Services.Users;

public sealed class UserService : IUserService
{
    // Regex chuẩn kiểm tra định dạng email
    private static readonly Regex EmailFormatRegex = new(
        @"^[a-zA-Z0-9._%+-]+@(?!.*(\.[a-zA-Z]{2,})\1$)[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Regex mật khẩu mạnh (>=8 ký tự, có hoa, thường, số, ký tự đặc biệt)
    private static readonly Regex StrongPasswordRegex = new(
        @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z0-9]).{8,128}$",
        RegexOptions.Compiled);

    private readonly IUserRepository _userRepository;
    private readonly IPilotRepository _pilotRepository;
    private readonly AppDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IMemoryCache _cache;

    public UserService(
        IUserRepository userRepository,
        IPilotRepository pilotRepository,
        AppDbContext context,
        IEmailService emailService,
        IMemoryCache cache)
    {
        _userRepository = userRepository;
        _pilotRepository = pilotRepository;
        _context = context;
        _emailService = emailService;
        _cache = cache;
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
            AvatarUrl = user.AvatarPath is null ? null : "/api/users/avatar?v=" + Uri.EscapeDataString(user.AvatarPath),
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

    // =========================================================================
    // CÁC CHỨC NĂNG MỚI: ĐỔI EMAIL & ĐỔI MẬT KHẨU
    // =========================================================================

    // 1. Yêu cầu đổi email (Gửi OTP sang email mới)
    public async Task<string> RequestChangeEmailAsync(
        Guid userId,
        ChangeEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        var cleanNewEmail = request.NewEmail?.Trim().ToLowerInvariant() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(cleanNewEmail) || !EmailFormatRegex.IsMatch(cleanNewEmail))
        {
            throw new Exception("Invalid new email address format.");
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
        {
            throw new Exception("User not found.");
        }

        if (!BC.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw new Exception("Current password is incorrect.");
        }

        var emailExists = await _context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email.ToLower() == cleanNewEmail, cancellationToken);

        if (emailExists)
        {
            throw new Exception("This email address is already registered by another account.");
        }

        var rateLimitKey = $"CHANGE_EMAIL_RATE_LIMIT_{userId}";
        if (_cache.TryGetValue(rateLimitKey, out _))
        {
            throw new Exception("Please wait 60 seconds before requesting a new verification code.");
        }

        var otpCode = Generate5CharacterCode();
        var cacheKey = $"CHANGE_EMAIL_OTP_{userId}";
        var cacheEntry = new PendingEmailChange
        {
            UserId = userId,
            NewEmail = cleanNewEmail,
            Code = otpCode,
            FailedAttempts = 0
        };

        _cache.Set(cacheKey, cacheEntry, TimeSpan.FromMinutes(10));
        _cache.Set(rateLimitKey, true, TimeSpan.FromSeconds(60));

        var emailBody = $@"
            <div style='font-family: Arial, sans-serif; padding: 20px; line-height: 1.6;'>
                <h2 style='color: #1a73e8;'>DroneOps Email Change Verification</h2>
                <p>Hello <b>{user.FullName}</b>,</p>
                <p>We received a request to change your account email to this address. Your verification code is:</p>
                <div style='background-color: #f1f3f4; padding: 12px 20px; border-radius: 8px; width: fit-content; margin: 16px 0;'>
                    <span style='font-size: 28px; font-weight: bold; letter-spacing: 6px; color: #1a73e8;'>{otpCode}</span>
                </div>
                <p style='color: #5f6368; font-size: 13px;'>This code will expire in 10 minutes. Do not share it with anyone.</p>
            </div>";

        await _emailService.SendEmailAsync(
            cleanNewEmail,
            "DroneOps - Email Change Verification Code",
            emailBody);

        return "Verification code has been sent to your new email address.";
    }

    // 2. Xác thực OTP và hoàn tất đổi email
    public async Task<bool> ConfirmChangeEmailAsync(
        Guid userId,
        VerifyChangeEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        var cleanNewEmail = request.NewEmail?.Trim().ToLowerInvariant() ?? string.Empty;
        var cleanCode = request.Otp?.Trim() ?? string.Empty;

        var cacheKey = $"CHANGE_EMAIL_OTP_{userId}";
        if (!_cache.TryGetValue(cacheKey, out PendingEmailChange? pending) || pending == null)
        {
            throw new Exception("Verification code has expired or does not exist. Please request a new code.");
        }

        if (!string.Equals(pending.NewEmail, cleanNewEmail, StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception("The provided email does not match the pending email change request.");
        }

        if (!string.Equals(pending.Code, cleanCode, StringComparison.OrdinalIgnoreCase))
        {
            pending.FailedAttempts++;
            if (pending.FailedAttempts >= 5)
            {
                _cache.Remove(cacheKey);
                throw new Exception("Too many incorrect attempts. Your verification code has been invalidated.");
            }
            _cache.Set(cacheKey, pending, TimeSpan.FromMinutes(10));
            throw new Exception($"Invalid verification code. You have {5 - pending.FailedAttempts} attempt(s) remaining.");
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
        {
            throw new Exception("User not found.");
        }

        user.Email = pending.NewEmail;
        _context.Users.Update(user);
        await _context.SaveChangesAsync(cancellationToken);

        _cache.Remove(cacheKey);
        return true;
    }

    // 3. Đổi mật khẩu
    public async Task<bool> ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || !StrongPasswordRegex.IsMatch(request.NewPassword))
        {
            throw new Exception("New password must be at least 8 characters long and contain at least one uppercase letter, one lowercase letter, one number, and one special character.");
        }

        if (!string.Equals(request.NewPassword, request.ConfirmPassword))
        {
            throw new Exception("Password confirmation does not match the new password.");
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
        {
            throw new Exception("User not found.");
        }

        if (!BC.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw new Exception("Current password is incorrect.");
        }

        user.PasswordHash = BC.HashPassword(request.NewPassword);
        _context.Users.Update(user);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    // Hàm phụ trợ sinh mã ngẫu nhiên 5 ký tự
    private string Generate5CharacterCode()
    {
        const string chars = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        var result = new char[5];
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[5];
        rng.GetBytes(bytes);

        for (int i = 0; i < 5; i++)
        {
            result[i] = chars[bytes[i] % chars.Length];
        }

        return new string(result);
    }

    private class PendingEmailChange
    {
        public Guid UserId { get; set; }
        public string NewEmail { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public int FailedAttempts { get; set; }
    }
}