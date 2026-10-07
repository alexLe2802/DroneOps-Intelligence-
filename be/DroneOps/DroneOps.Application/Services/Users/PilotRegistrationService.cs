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
            throw new Exception("Không tìm thấy đơn đăng ký phi công.");
        }

        if (!string.Equals(registration.Status, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception($"Đơn đăng ký đang ở trạng thái '{registration.Status}', không thể phê duyệt.");
        }

        var cleanEmail = registration.Email.Trim().ToLowerInvariant();

        // Tìm hoặc khởi tạo Role "Pilot"
        var pilotRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name.ToLower() == "pilot");
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
                Name = "Pilot",
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
        var emailSubject = "[DroneOps] Đơn đăng ký Pilot đã được phê duyệt - Mã OTP xác thực lần đầu";
        var emailBody = $@"
            <div style='font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #e0e0e0; border-radius: 8px; padding: 20px;'>
                <h2 style='color: #1a73e8; border-bottom: 2px solid #1a73e8; padding-bottom: 10px;'>Chúc mừng {registration.FullName}!</h2>
                <p>Đơn đăng ký tham gia đội ngũ Pilot tại hệ thống <strong>DroneOps Intelligence</strong> của bạn đã được phê duyệt thành công.</p>
                <p>Để đảm bảo an toàn, hệ thống yêu cầu xác thực hai lớp (2FA) trong lần đăng nhập đầu tiên:</p>
                <div style='background-color: #f1f3f4; padding: 15px; border-left: 4px solid #1a73e8; border-radius: 4px; margin: 15px 0;'>
                    <p style='margin: 0 0 8px 0;'><strong>Tài khoản (Email):</strong> {registration.Email}</p>
                    <p style='margin: 0 0 8px 0;'><strong>Mật khẩu:</strong> <em>Mật khẩu bạn đã tự đặt khi nộp đơn đăng ký</em></p>
                    <p style='margin: 0;'><strong>Mã OTP xác thực đăng nhập lần đầu:</strong> <span style='font-size: 24px; font-weight: bold; color: #d93025; letter-spacing: 4px;'>{otpCode}</span></p>
                </div>
                <p><em>Vui lòng truy cập màn hình Đăng nhập lần đầu (First-time Login), nhập Email, Mật khẩu đã đăng ký và mã OTP ở trên để kích hoạt tài khoản.</em></p>
                <br/>
                <p style='margin: 0; color: #70757a; font-size: 13px;'>Trân trọng,<br/><strong>Ban Quản Trị DroneOps Intelligence</strong></p>
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
            throw new Exception("Tài khoản hoặc mật khẩu không chính xác.");
        }

        // 2. Xác thực mật khẩu cá nhân mà Pilot đã tự đặt
        var isPasswordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
        if (!isPasswordValid)
        {
            throw new Exception("Mật khẩu không chính xác.");
        }

        // 3. Kiểm tra mã OTP gửi về email
        var cacheKey = $"PILOT_FIRST_LOGIN_OTP_{cleanEmail}";
        if (!_cache.TryGetValue(cacheKey, out string? cachedOtp) || string.IsNullOrEmpty(cachedOtp))
        {
            throw new Exception("Mã OTP đã hết hạn hoặc tài khoản này đã hoàn tất kích hoạt lần đầu. Vui lòng đăng nhập bình thường.");
        }

        if (!string.Equals(cachedOtp, request.Otp.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception("Mã OTP không chính xác. Vui lòng kiểm tra lại email.");
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
                Role = user.Role?.Name ?? "Pilot"
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
            throw new Exception("Vui lòng cung cấp lý do từ chối đơn đăng ký.");
        }

        var registration = await _repository.GetByIdAsync(registrationId);
        if (registration == null)
        {
            throw new Exception("Không tìm thấy đơn đăng ký phi công.");
        }

        if (!string.Equals(registration.Status, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception($"Đơn đăng ký đang ở trạng thái '{registration.Status}', không thể từ chối.");
        }

        registration.Status = "Rejected";
        registration.RejectReason = request.Reason.Trim();
        registration.ReviewedAt = DateTime.UtcNow;

        await _repository.SaveChangesAsync();

        var emailSubject = "[DroneOps] Thông báo về kết quả đơn đăng ký Pilot";
        var emailBody = $@"
            <div style='font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #e0e0e0; border-radius: 8px; padding: 20px;'>
                <h2 style='color: #d93025; border-bottom: 2px solid #d93025; padding-bottom: 10px;'>Kính gửi {registration.FullName},</h2>
                <p>Cảm ơn bạn đã quan tâm và gửi đơn đăng ký tham gia đội ngũ Pilot tại hệ thống <strong>DroneOps Intelligence</strong>.</p>
                <p>Sau khi xem xét hồ sơ bằng lái và các thông tin liên quan, chúng tôi rất tiếc phải thông báo rằng đơn đăng ký của bạn <strong>chưa được phê duyệt</strong> vào thời điểm này.</p>
                <div style='background-color: #fce8e6; padding: 15px; border-left: 4px solid #d93025; border-radius: 4px; margin: 15px 0;'>
                    <p style='margin: 0 0 5px 0;'><strong>Lý do từ chối:</strong></p>
                    <p style='margin: 0; color: #c5221f; font-weight: 500;'>{request.Reason.Trim()}</p>
                </div>
                <p>Nếu bạn cần hỗ trợ thêm thông tin hoặc muốn bổ sung hồ sơ để xem xét lại, vui lòng phản hồi email này.</p>
                <br/>
                <p style='margin: 0; color: #70757a; font-size: 13px;'>Trân trọng,<br/><strong>Ban Quản Trị DroneOps Intelligence</strong></p>
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
            new(ClaimTypes.Role, user.Role?.Name ?? "Pilot"),
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