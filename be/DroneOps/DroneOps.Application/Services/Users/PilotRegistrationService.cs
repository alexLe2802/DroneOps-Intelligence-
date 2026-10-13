using DroneOps.Domain;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;
using DroneOps.Application.DTOs.Request.Users;
using DroneOps.Application.DTOs.Response.Auth;
using DroneOps.Application.DTOs.Response.Pages;
using DroneOps.Application.DTOs.Response.Users;
using DroneOps.Application.Interfaces.Auth;
using DroneOps.Application.Interfaces.Users;
using DroneOps.Application.Settings;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DroneOps.Application.Services.Users;

public class PilotRegistrationService : IPilotRegistrationService
{
    private readonly IPilotRegistrationRepository _repository;
    private readonly AppDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IMemoryCache _cache;
    private readonly JwtSettings _jwtSettings;

    public PilotRegistrationService(
        IPilotRegistrationRepository repository,
        AppDbContext context,
        IEmailService emailService,
        IMemoryCache cache,
        IOptions<JwtSettings> jwtSettings)
    {
        _repository = repository;
        _context = context;
        _emailService = emailService;
        _cache = cache;
        _jwtSettings = jwtSettings.Value;
    }

    // =========================================================================
    // 1. NỘP ĐƠN ĐĂNG KÝ PILOT
    // Mật khẩu do chính Pilot nhập được băm và lưu trực tiếp vào phiếu đăng ký
    // =========================================================================
    public async Task RegisterAsync(CreatePilotRegistrationRequest request)
    {
        var existed = await _repository.EmailExistsAsync(request.Email);
        if (existed)
        {
            throw new Exception("Email already registered.");
        }

        var registration = new PilotRegistration
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password), // Mật khẩu gốc được mã hóa
            PhoneNumber = request.PhoneNumber,
            DateOfBirth = request.DateOfBirth,
            DroneType = request.DroneType,
            DroneModel = request.DroneModel,
            PilotLicenseNo = request.PilotLicenseNo,
            LicenseIssuedDate = request.LicenseIssuedDate,
            LicenseExpiredDate = request.LicenseExpiredDate,
            ExperienceYears = request.ExperienceYears,
            UsagePurpose = request.UsagePurpose,
            Description = request.Description,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        await _repository.AddAsync(registration);
        await _repository.SaveChangesAsync();
    }

    // =========================================================================
    // 2. XEM CHI TIẾT ĐƠN ĐĂNG KÝ
    // =========================================================================
    public async Task<PilotRegistrationDetailResponse?> GetDetailAsync(Guid id)
    {
        var registration = await _repository.GetByIdAsync(id);
        if (registration == null)
        {
            return null;
        }

        return new PilotRegistrationDetailResponse
        {
            Id = registration.Id,
            FullName = registration.FullName,
            Email = registration.Email,
            PhoneNumber = registration.PhoneNumber,
            DateOfBirth = registration.DateOfBirth,
            DroneType = registration.DroneType,
            DroneModel = registration.DroneModel,
            PilotLicenseNo = registration.PilotLicenseNo,
            LicenseIssuedDate = registration.LicenseIssuedDate,
            LicenseExpiredDate = registration.LicenseExpiredDate,
            ExperienceYears = registration.ExperienceYears,
            UsagePurpose = registration.UsagePurpose,
            Description = registration.Description,
            Status = registration.Status,
            RejectReason = registration.RejectReason,
            ReviewedBy = registration.ReviewedBy,
            ReviewedAt = registration.ReviewedAt,
            CreatedAt = registration.CreatedAt
        };
    }

    // =========================================================================
    // 3. LẤY DANH SÁCH ĐƠN ĐĂNG KÝ
    // =========================================================================
    public async Task<PagedPilotRegistrationResponse> GetAllAsync(GetPilotRegistrationsRequest request)
    {
        var result = await _repository.GetAllAsync(
            request.PageNumber,
            request.PageSize,
            request.Search,
            request.Status);

        return new PagedPilotRegistrationResponse
        {
            Items = result.Items.Select(x => new PilotRegistrationResponse
            {
                Id = x.Id,
                FullName = x.FullName,
                Email = x.Email,
                PhoneNumber = x.PhoneNumber,
                DroneType = x.DroneType,
                DroneModel = x.DroneModel,
                PilotLicenseNo = x.PilotLicenseNo,
                UsagePurpose = x.UsagePurpose,
                Status = x.Status,
                CreatedAt = x.CreatedAt
            }).ToList(),

            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            TotalItems = result.TotalCount,
            TotalPages = (int)Math.Ceiling(
                result.TotalCount / (double)request.PageSize)
        };
    }

    // =========================================================================
    // 4. PHÊ DUYỆT ĐƠN (APPROVE)
    // - Dùng lại đúng mật khẩu gốc của Pilot khi nộp đơn để tạo tài khoản User
    // - Sinh mã OTP 6 số xác thực và gửi qua email cho lần đăng nhập đầu tiên
    // =========================================================================
    public async Task<bool> ApprovePilotRegistrationAsync(Guid registrationId, ApprovePilotRegistrationRequest? request = null)
    {
        var registration = await _repository.GetByIdAsync(registrationId);
        if (registration == null)
        {
            throw new Exception("Pilot registration not found.");
        }

        if (!string.Equals(registration.Status, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception($"Registration is currently in '{registration.Status}' status and cannot be approved.");
        }

        var cleanEmail = registration.Email.Trim().ToLowerInvariant();

        // Tìm hoặc khởi tạo Role "uav_operator"
        var pilotRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name.ToLower() == "uav_operator");
        Guid roleId;
        if (pilotRole != null)
        {
            roleId = pilotRole.Id;
        }
        else
        {
            var newRole = new Role
            {
                Id = Guid.NewGuid(),
                Name = "uav_operator",
                CreatedAt = DateTimeOffset.UtcNow
            };
            await _context.Roles.AddAsync(newRole);
            await _context.SaveChangesAsync();
            roleId = newRole.Id;
        }

        // Tạo tài khoản User VỚI ĐÚNG MẬT KHẨU GỐC (registration.PasswordHash)
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail);
        if (user == null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                Email = cleanEmail,
                FullName = registration.FullName,
                PasswordHash = registration.PasswordHash, // Mật khẩu lúc đăng ký
                RoleId = roleId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await _context.Users.AddAsync(user);
        }
        else
        {
            user.RoleId = roleId;
            user.PasswordHash = registration.PasswordHash;
            _context.Users.Update(user);
        }
        await _context.SaveChangesAsync();

        // Tạo bản ghi hồ sơ Pilot liên kết với User
        var pilot = await _context.Set<Pilot>().FirstOrDefaultAsync(p => p.UserId == user.Id);
        if (pilot == null)
        {
            pilot = new Pilot
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                PhoneNumber = registration.PhoneNumber,
                DateOfBirth = registration.DateOfBirth,
                PilotLicenseNo = registration.PilotLicenseNo,
                LicenseIssuedDate = registration.LicenseIssuedDate,
                LicenseExpiredDate = registration.LicenseExpiredDate,
                ExperienceYears = registration.ExperienceYears ?? 0,
                CreatedAt = DateTime.UtcNow
            };
            await _context.Set<Pilot>().AddAsync(pilot);
        }
        else
        {
            pilot.PhoneNumber = registration.PhoneNumber;
            pilot.DateOfBirth = registration.DateOfBirth;
            pilot.PilotLicenseNo = registration.PilotLicenseNo;
            pilot.LicenseIssuedDate = registration.LicenseIssuedDate;
            pilot.LicenseExpiredDate = registration.LicenseExpiredDate;
            pilot.ExperienceYears = registration.ExperienceYears ?? 0;
            _context.Set<Pilot>().Update(pilot);
        }

        // Cập nhật trạng thái đơn thành Approved
        registration.Status = "Approved";
        registration.ReviewedAt = DateTime.UtcNow;
        registration.RejectReason = null;
        await _repository.SaveChangesAsync();

        // Sinh mã OTP 6 số xác thực đăng nhập lần đầu
        var otpCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var cacheKey = $"PILOT_FIRST_LOGIN_OTP_{cleanEmail}";

        // Lưu mã OTP vào Cache (hiệu lực 24 giờ để Pilot có thời gian vào kích hoạt)
        _cache.Set(cacheKey, otpCode, TimeSpan.FromHours(24));

        // Gửi email chúc mừng và cung cấp mã OTP kích hoạt lần đầu
        var emailSubject = "[DroneOps] Pilot Registration Approved - First-Time Login OTP";
        var emailBody = $@"
            <div style='font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #e0e0e0; border-radius: 8px; padding: 20px;'>
                <h2 style='color: #1a73e8; border-bottom: 2px solid #1a73e8; padding-bottom: 10px;'>Congratulations, {registration.FullName}!</h2>
                <p>Your application to join the <strong>DroneOps Intelligence</strong> pilot team has been approved.</p>
                <p>For security purposes, first-time authentication requires two-factor verification (2FA):</p>
                <div style='background-color: #f1f3f4; padding: 15px; border-left: 4px solid #1a73e8; border-radius: 4px; margin: 15px 0;'>
                    <p style='margin: 0 0 8px 0;'><strong>Account (Email):</strong> {registration.Email}</p>
                    <p style='margin: 0 0 8px 0;'><strong>Password:</strong> <em>The password you created during registration</em></p>
                    <p style='margin: 0;'><strong>First-Time Login OTP:</strong> <span style='font-size: 24px; font-weight: bold; color: #d93025; letter-spacing: 4px;'>{otpCode}</span></p>
                </div>
                <p><em>Please proceed to the First-Time Login screen, enter your registered Email, Password, and the OTP code above to activate your account.</em></p>
                <br/>
                <p style='margin: 0; color: #70757a; font-size: 13px;'>Best regards,<br/><strong>DroneOps Intelligence Team</strong></p>
            </div>";

        await _emailService.SendEmailAsync(registration.Email, emailSubject, emailBody);

        return true;
    }

    // =========================================================================
    // 5. ĐĂNG NHẬP LẦN ĐẦU CHO PILOT
    // Pilot nhập: Email + Mật khẩu lúc đăng ký + Mã OTP từ email
    // Sau khi thành công, hệ thống xóa OTP và trả về Token JWT
    // =========================================================================
    public async Task<LoginResponse> FirstTimeLoginAsync(PilotFirstTimeLoginRequest request, CancellationToken cancellationToken = default)
    {
        var cleanEmail = request.Email.Trim().ToLowerInvariant();

        // 1. Kiểm tra tài khoản tồn tại
        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail, cancellationToken);

        if (user == null)
        {
            throw new Exception("Invalid email or password.");
        }

        // 2. Xác thực mật khẩu cá nhân mà Pilot đã tự đặt
        var isPasswordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
        if (!isPasswordValid)
        {
            throw new Exception("Invalid password.");
        }

        // 3. Kiểm tra mã OTP gửi về email
        var cacheKey = $"PILOT_FIRST_LOGIN_OTP_{cleanEmail}";
        if (!_cache.TryGetValue(cacheKey, out string? cachedOtp) || string.IsNullOrEmpty(cachedOtp))
        {
            throw new Exception("OTP code has expired or this account has already completed first-time activation. Please log in normally.");
        }

        if (!string.Equals(cachedOtp, request.Otp.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception("Invalid OTP code. Please check your email and try again.");
        }

        // 4. Xác thực thành công -> Xóa mã OTP khỏi Cache (các lần sau không cần OTP nữa)
        _cache.Remove(cacheKey);

        // 5. Tạo JWT Token đăng nhập trực tiếp vào hệ thống
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes);
        var token = GenerateJwtToken(user, expiresAt);

        return new LoginResponse
        {
            AccessToken = token,
            ExpiresAt = expiresAt,
            User = new LoginUserResponse
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Role = CanonicalRoles.Normalize(user.Role?.Name ?? "uav_operator")
            }
        };
    }

    // =========================================================================
    // 6. TỪ CHỐI ĐƠN PILOT KÈM LÝ DO
    // =========================================================================
    public async Task<bool> RejectPilotRegistrationAsync(Guid registrationId, RejectPilotRegistrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new Exception("Please provide a rejection reason.");
        }

        var registration = await _repository.GetByIdAsync(registrationId);
        if (registration == null)
        {
            throw new Exception("Pilot registration not found.");
        }

        if (!string.Equals(registration.Status, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception($"Registration is currently in '{registration.Status}' status and cannot be rejected.");
        }

        registration.Status = "Rejected";
        registration.RejectReason = request.Reason.Trim();
        registration.ReviewedAt = DateTime.UtcNow;

        await _repository.SaveChangesAsync();

        var emailSubject = "[DroneOps] Notice of Pilot Application Result";
        var emailBody = $@"
            <div style='font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #e0e0e0; border-radius: 8px; padding: 20px;'>
                <h2 style='color: #d93025; border-bottom: 2px solid #d93025; padding-bottom: 10px;'>Pilot Application Status Notice</h2>
                <p>Dear {registration.FullName},</p>
                <p>Thank you for your interest in joining the <strong>DroneOps Intelligence</strong> pilot team.</p>
                <p>After reviewing your license and application details, we regret to inform you that your registration could not be approved at this time.</p>
                <div style='background-color: #fce8e6; padding: 15px; border-left: 4px solid #d93025; border-radius: 4px; margin: 15px 0;'>
                    <p style='margin: 0 0 5px 0;'><strong>Reason for Rejection:</strong></p>
                    <p style='margin: 0; color: #c5221f; font-weight: 500;'>{request.Reason.Trim()}</p>
                </div>
                <p>If you need further assistance or wish to appeal this decision, please reply to this email.</p>
                <br/>
                <p style='margin: 0; color: #70757a; font-size: 13px;'>Best regards,<br/><strong>DroneOps Intelligence Team</strong></p>
            </div>";

        await _emailService.SendEmailAsync(registration.Email, emailSubject, emailBody);

        return true;
    }

    // Hàm phụ trợ tạo JWT Token chuẩn bảo mật
    private string GenerateJwtToken(User user, DateTime expiresAt)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, CanonicalRoles.Normalize(user.Role?.Name ?? "uav_operator")),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}