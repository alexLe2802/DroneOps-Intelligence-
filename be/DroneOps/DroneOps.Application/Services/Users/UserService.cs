using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using System.Text.RegularExpressions;
using DroneOps.Application.DTOs.Request.Users;
using DroneOps.Application.DTOs.Response.Common;
using DroneOps.Application.DTOs.Response.Users;
using DroneOps.Application.Interfaces.Auth;
using DroneOps.Application.Interfaces.Users;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using DroneOps.Persistence.Data;
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
    private readonly IEmailService _emailService;
    private readonly IMemoryCache _cache;
    private readonly AppDbContext? _db;

    public UserService(
        IUserRepository userRepository,
        IPilotRepository pilotRepository,
        IEmailService emailService,
        IMemoryCache cache)
        : this(userRepository,pilotRepository,emailService,cache,null) { }

    public UserService(IUserRepository userRepository,IPilotRepository pilotRepository,IEmailService emailService,IMemoryCache cache,AppDbContext? db)
    {
        _userRepository = userRepository;
        _pilotRepository = pilotRepository;
        _emailService = emailService;
        _cache = cache;
        _db = db;
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

        if(request.ExpectedVersion!=user.ProfileVersion)
            throw new DbUpdateConcurrencyException("Profile changed since it was loaded.");
        user.FullName = fullName;
        user.ProfileVersion++;

        await _userRepository.SaveChangesAsync(
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
            ProfileVersion=user.ProfileVersion,
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role.Name,
            CreatedAt = user.CreatedAt
        };
    }

    public async Task<PilotProfileResponse?>GetPilotProfileAsync(
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

    public async Task<PagedResponse<UserProfileResponse>>GetAllUsersAsync(
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
                .Select(user => {
                    var profile = MapToProfileResponse(user);
                    profile.AvatarUrl = user.AvatarPath is null ? null : $"/api/users/{user.Id}/avatar?v=" + Uri.EscapeDataString(user.AvatarPath);
                    return profile;
                })
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

    public async Task<UpdatePilotProfileResponse?> UpdatePilotProfileAsync(
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

            if (hasPendingRequest && _db is not null)
            {
                var prior=await _db.PilotUpdateRequests.SingleAsync(x=>x.PilotId==pilot.Id&&x.Status=="Pending",cancellationToken);
                prior.Status="Superseded";
            }
            else if (hasPendingRequest)
            {
                throw new InvalidOperationException(
                    "You already have a pending pilot update request.");
            }
        }

        // Theo dõi xem thông tin thường có thực sự thay đổi không
        bool profileChanged = false;

        // Có gửi PhoneNumber thì mới cập nhật.
        // Chuỗi rỗng được hiểu là xóa số điện thoại.
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

        // Không gửi thì giữ nguyên.
        if (request.DateOfBirth.HasValue &&
            pilot.DateOfBirth != request.DateOfBirth.Value)
        {
            pilot.DateOfBirth =
                request.DateOfBirth.Value;

            profileChanged = true;
        }

        // Không gửi thì giữ nguyên.
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
            try { await _pilotRepository.SaveChangesAsync(cancellationToken); }
            catch(DbUpdateException e) when(e.InnerException is PostgresException {SqlState:PostgresErrorCodes.UniqueViolation})
            { throw new InvalidOperationException("A concurrent license request is already pending.",e); }
        }

        return new UpdatePilotProfileResponse
        {
            UserId = pilot.UserId,
            PilotId = pilot.Id,

            FullName = pilot.User.FullName,
            Email = pilot.User.Email,

            PhoneNumber = pilot.PhoneNumber,
            DateOfBirth = pilot.DateOfBirth,
            ExperienceYears = pilot.ExperienceYears,

            

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
    

    public async Task< PagedResponse<PilotUpdateRequestResponse>>
  GetAllPilotRequestsAsync(PilotUpdateRequestFilterRequest request,CancellationToken cancellationToken = default)
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

        string? status =
            string.IsNullOrWhiteSpace(request.Status)
                ? null
                : request.Status.Trim();

        (
            IReadOnlyList<PilotUpdateRequest> Items,
            int TotalCount
        )
            result =
                await _pilotRepository
                    .GetAllPilotRequestsAsync(
                        pageNumber,
                        pageSize,
                        search,
                        status,
                        cancellationToken);

        IReadOnlyList<PilotUpdateRequestResponse>
            items =
                result.Items
                    .Select(
                        MapPilotUpdateRequestResponse)
                    .ToList();

        int totalPages =
            result.TotalCount == 0
                ? 0
                : (int)Math.Ceiling(
                    result.TotalCount /
                    (double)pageSize);

        return new PagedResponse<
            PilotUpdateRequestResponse>
        {
            Items = items,

            PageNumber = pageNumber,

            PageSize = pageSize,

            TotalItems = result.TotalCount,

            TotalPages = totalPages
        };
    }

    public async Task<PilotUpdateRequestResponse?>GetPilotUpdateRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        PilotUpdateRequest? updateRequest =
            await _pilotRepository
                .GetUpdateRequestForReviewAsync(
                    requestId,
                    cancellationToken);

        if (updateRequest is null)
        {
            return null;
        }

        return MapPilotUpdateRequestResponse(
            updateRequest);
    }

    public async Task<PilotUpdateRequestResponse?> ApprovePilotUpdateRequestAsync(
        Guid requestId,
        Guid reviewerId,
        CancellationToken cancellationToken = default)
    {
        PilotUpdateRequest? updateRequest =
            await _pilotRepository
                .GetUpdateRequestForReviewAsync(
                    requestId,
                    cancellationToken);

        if (updateRequest is null)
        {
            return null;
        }

        if (!string.Equals(
                updateRequest.Status,
                "Pending",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Only pending pilot update requests can be approved.");
        }

        if(updateRequest.Pilot.UserId==reviewerId)
            throw new InvalidOperationException("The request author cannot review their own license change.");

        Pilot pilot =
            updateRequest.Pilot;

        // Chính thức cập nhật thông tin giấy phép vào Pilot
        pilot.PilotLicenseNo =
            updateRequest.PilotLicenseNo;

        pilot.LicenseIssuedDate =
            updateRequest.LicenseIssuedDate;

        pilot.LicenseExpiredDate =
            updateRequest.LicenseExpiredDate;

       
        // Lấy bộ ảnh license hiện tại của Pilot
        List<LicenseImage> oldImages =
            await _pilotRepository
                .GetCurrentPilotLicenseImagesAsync(
                    pilot.Id,
                    cancellationToken);

        // Gỡ liên kết các ảnh license cũ khỏi Pilot
        foreach (LicenseImage oldImage in oldImages)
        {
            oldImage.PilotId = null;
        }

        // Gắn bộ ảnh license mới đã được duyệt vào Pilot
        foreach (LicenseImage newImage
                 in updateRequest.LicenseImages)
        {
            newImage.PilotId = pilot.Id;
        }

        updateRequest.Status =
            "Approved";

        updateRequest.RejectReason =
            null;

        updateRequest.ReviewedBy =
            reviewerId;

        updateRequest.ReviewedAt =
            DateTime.UtcNow;

        if(_db is not null)_db.PilotUpdateDecisionAudits.Add(new PilotUpdateDecisionAudit
        {Id=Guid.NewGuid(),RequestId=updateRequest.Id,ActorId=reviewerId,TargetUserId=pilot.UserId,Result="Approved",OccurredAt=DateTimeOffset.UtcNow});

        try { await _pilotRepository.SaveChangesAsync(cancellationToken); }
        catch(DbUpdateException e) when(e.InnerException is PostgresException {SqlState:PostgresErrorCodes.UniqueViolation})
        {throw new InvalidOperationException("This decision is stale or was completed concurrently.",e);}

        return MapPilotUpdateRequestResponse(
            updateRequest);
    }
    public async Task<PilotUpdateRequestResponse?>RejectPilotUpdateRequestAsync(
        Guid requestId,
        Guid reviewerId,
        RejectPilotUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        PilotUpdateRequest? updateRequest =
            await _pilotRepository
                .GetUpdateRequestForReviewAsync(
                    requestId,
                    cancellationToken);

        if (updateRequest is null)
        {
            return null;
        }

        if (!string.Equals(
                updateRequest.Status,
                "Pending",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Only pending pilot update requests can be rejected.");
        }

        if(updateRequest.Pilot.UserId==reviewerId)
            throw new InvalidOperationException("The request author cannot review their own license change.");

        updateRequest.Status =
            "Rejected";

        updateRequest.RejectReason =
            request.RejectReason.Trim();

        updateRequest.ReviewedBy =
            reviewerId;

        updateRequest.ReviewedAt =
            DateTime.UtcNow;

        if(_db is not null)_db.PilotUpdateDecisionAudits.Add(new PilotUpdateDecisionAudit
        {Id=Guid.NewGuid(),RequestId=updateRequest.Id,ActorId=reviewerId,TargetUserId=updateRequest.Pilot.UserId,Result="Rejected",OccurredAt=DateTimeOffset.UtcNow});

        // Reject không update Pilot
        // Reject không set LicenseImage.PilotId

        try { await _pilotRepository.SaveChangesAsync(cancellationToken); }
        catch(DbUpdateException e) when(e.InnerException is PostgresException {SqlState:PostgresErrorCodes.UniqueViolation})
        {throw new InvalidOperationException("This decision is stale or was completed concurrently.",e);}

        return MapPilotUpdateRequestResponse(
            updateRequest);
    }
    private static PilotUpdateRequestResponse
    MapPilotUpdateRequestResponse(
        PilotUpdateRequest request)
    {
        return new PilotUpdateRequestResponse
        {
            Id =
                request.Id,

            PilotId =
                request.PilotId,

            UserId =
                request.Pilot.UserId,

            FullName =
                request.Pilot.User.FullName,

            Email =
                request.Pilot.User.Email,

            PilotLicenseNo =
                request.PilotLicenseNo,

            LicenseIssuedDate =
                request.LicenseIssuedDate,

            LicenseExpiredDate =
                request.LicenseExpiredDate,

            Status =
                request.Status,

            RejectReason =
                request.RejectReason,

            ReviewedBy =
                request.ReviewedBy,

            ReviewedAt =
                request.ReviewedAt,

            CreatedAt =
                request.CreatedAt,

            LicenseImageUrls =
                request.LicenseImages
                    .Select(x => x.ImageUrl)
                    .ToList()
        };
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

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new Exception("User not found.");
        }

        if (!BC.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw new Exception("Current password is incorrect.");
        }

        if(_db is not null)
            return await RequestPersistentEmailChallengeAsync(user,cleanNewEmail,cancellationToken);

        var emailExists = await _userRepository.EmailExistsAsync(
            cleanNewEmail,
            cancellationToken: cancellationToken);

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

        _cache.Set(cacheKey, cacheEntry, TimeSpan.FromMinutes(10));
        _cache.Set(rateLimitKey, true, TimeSpan.FromSeconds(60));
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

        if(_db is not null)
            return await ConfirmPersistentEmailChallengeAsync(userId,cleanNewEmail,cleanCode,cancellationToken);

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
            throw new Exception($"Invalid verification code. You have {5 - pending.FailedAttempts} attempt(s) remaining.");
        }

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new Exception("User not found.");
        }

        if (await _userRepository.EmailExistsAsync(pending.NewEmail, userId, cancellationToken))
            throw new Exception("This email address is already registered by another account.");
        user.Email = pending.NewEmail;
        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync(cancellationToken);

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

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new Exception("User not found.");
        }

        if (!BC.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw new Exception("Current password is incorrect.");
        }

        user.PasswordHash = BC.HashPassword(request.NewPassword);
        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync(cancellationToken);

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

    private static string ChallengeHash(Guid id,string code)=>Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes($"{id:N}:{code.Trim().ToUpperInvariant()}")));

    private async Task<string> RequestPersistentEmailChallengeAsync(User user,string newEmail,CancellationToken ct)
    {
        if(_db is null)throw new InvalidOperationException();
        if(await _db.Users.AsNoTracking().AnyAsync(x=>x.Email.ToLower()==newEmail&&x.Id!=user.Id,ct))
            throw new Exception("This email address is already registered by another account.");
        var code=Generate5CharacterCode();var id=Guid.NewGuid();var now=DateTimeOffset.UtcNow;
        await _db.EmailChangeChallenges.Where(x=>x.AccountId==user.Id&&x.Status=="Pending")
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Superseded"),ct);
        _db.EmailChangeChallenges.Add(new EmailChangeChallenge {Id=id,AccountId=user.Id,NewEmail=newEmail,
            CodeHash=ChallengeHash(id,code),CreatedAt=now,ExpiresAt=now.AddMinutes(10),Status="Pending"});
        _db.AccountWorkflowAudits.Add(new AccountWorkflowAudit {ActorId=user.Id,TargetId=user.Id,
            Action="email.challenge.requested",Result="Pending",OccurredAt=now,
            DetailsJson=JsonSerializer.Serialize(new {challengeId=id,newEmail})});
        await _db.SaveChangesAsync(ct);
        var body=$"<span style='font-size: 28px; font-weight: bold'>{code}</span>";
        try{await _emailService.SendEmailAsync(newEmail,"DroneOps - Email Change Verification Code",body);}
        catch
        {
            var challenge=await _db.EmailChangeChallenges.SingleAsync(x=>x.Id==id,ct);challenge.Status="DeliveryFailed";
            _db.AccountWorkflowAudits.Add(new AccountWorkflowAudit {ActorId=user.Id,TargetId=user.Id,
                Action="email.challenge.delivery",Result="Failed",OccurredAt=DateTimeOffset.UtcNow,
                DetailsJson=JsonSerializer.Serialize(new {challengeId=id})});
            await _db.SaveChangesAsync(ct);throw;
        }
        return "Verification code has been sent to your new email address.";
    }

    private async Task<bool> ConfirmPersistentEmailChallengeAsync(Guid userId,string newEmail,string code,CancellationToken ct)
    {
        if(_db is null)throw new InvalidOperationException();
        string? failure=null;
        try
        {
            await _db.Database.CreateExecutionStrategy().ExecuteAsync(async()=>
            {
                _db.ChangeTracker.Clear();
                await using var tx=await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
                var challenge=await _db.EmailChangeChallenges.FromSqlInterpolated(
                    $"SELECT * FROM droneops.\"EmailChangeChallenge\" WHERE \"AccountId\"={userId} AND lower(\"NewEmail\")={newEmail} AND \"Status\"='Pending' FOR UPDATE")
                    .SingleOrDefaultAsync(ct);
                if(challenge is null){failure="Verification code has expired or does not exist.";await tx.CommitAsync(ct);return;}
                if(challenge.ExpiresAt<=DateTimeOffset.UtcNow){challenge.Status="Expired";failure="Verification code has expired.";await _db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return;}
                var actual=Convert.FromHexString(ChallengeHash(challenge.Id,code));var expected=Convert.FromHexString(challenge.CodeHash);
                if(!CryptographicOperations.FixedTimeEquals(actual,expected))
                {
                    challenge.FailedAttempts++;if(challenge.FailedAttempts>=5)challenge.Status="Locked";
                    _db.AccountWorkflowAudits.Add(new AccountWorkflowAudit {ActorId=userId,TargetId=userId,
                        Action="email.challenge.confirm",Result="Failed",OccurredAt=DateTimeOffset.UtcNow,
                        DetailsJson=JsonSerializer.Serialize(new {challengeId=challenge.Id,challenge.FailedAttempts})});
                    failure=challenge.Status=="Locked"?"Too many incorrect attempts.":"Invalid verification code.";
                    await _db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return;
                }
                var account=await _db.Users.FromSqlInterpolated($"SELECT * FROM public.\"User\" WHERE \"Id\"={userId} FOR UPDATE").SingleAsync(ct);
                if(await _db.Users.AsNoTracking().AnyAsync(x=>x.Id!=userId&&x.Email.ToLower()==newEmail,ct))
                {
                    _db.AccountWorkflowAudits.Add(new AccountWorkflowAudit {ActorId=userId,TargetId=userId,
                        Action="email.changed",Result="Conflict",OccurredAt=DateTimeOffset.UtcNow,
                        DetailsJson=JsonSerializer.Serialize(new {challengeId=challenge.Id,newEmail})});
                    failure="This email address is already registered by another account.";await _db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return;
                }
                account.Email=newEmail;account.ProfileVersion++;challenge.Status="Consumed";challenge.ConsumedAt=DateTimeOffset.UtcNow;
                _db.AccountWorkflowAudits.Add(new AccountWorkflowAudit {ActorId=userId,TargetId=userId,
                    Action="email.changed",Result="Succeeded",OccurredAt=DateTimeOffset.UtcNow,
                    DetailsJson=JsonSerializer.Serialize(new {challengeId=challenge.Id,newEmail})});
                await _db.SaveChangesAsync(ct);
                await _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE droneops.auth_sessions SET revoked_at=now() WHERE account_id={userId} AND revoked_at IS NULL",ct);
                await tx.CommitAsync(ct);
            });
        }
        catch(DbUpdateException e) when(e.InnerException is PostgresException {SqlState:PostgresErrorCodes.UniqueViolation})
        {throw new Exception("This email address is already registered by another account.");}
        if(failure is not null)throw new Exception(failure);return true;
    }

    private class PendingEmailChange
    {
        public Guid UserId { get; set; }
        public string NewEmail { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public int FailedAttempts { get; set; }
    }
}
