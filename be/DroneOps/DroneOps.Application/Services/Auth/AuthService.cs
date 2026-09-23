using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DroneOps.Application.DTOs.Auth;
using DroneOps.Application.Settings;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using BC = BCrypt.Net.BCrypt;
using DroneOps.Application.Interfaces.Auth;

namespace DroneOps.Application.Services.Auth;

public class AuthService : IAuthService
{
    private static readonly Regex EmailFormatRegex = new(
        @"^[a-zA-Z0-9._%+-]+@(?!.*(\.[a-zA-Z]{2,})\1$)[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex StrongPasswordRegex = new(
        @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z0-9]).{8,128}$",
        RegexOptions.Compiled);

    private readonly IUserRepository _userRepository;
    private readonly AppDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IMemoryCache _cache;
    private readonly JwtSettings _jwtSettings;

    public AuthService(
        IUserRepository userRepository,
        AppDbContext context,
        IEmailService emailService,
        IMemoryCache cache,
        IOptions<JwtSettings> jwtSettings)
    {
        _userRepository = userRepository;
        _context = context;
        _emailService = emailService;
        _cache = cache;
        _jwtSettings = jwtSettings.Value;
    }

    public async Task<LoginResponse?> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var cleanEmail = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;

        var user = await _userRepository.GetByEmailWithRoleAsync(
            cleanEmail,
            cancellationToken);

        if (user is null)
        {
            return null;
        }

        var validPassword = BC.Verify(
            request.Password,
            user.PasswordHash);

        if (!validPassword)
        {
            return null;
        }

        var expiresAt = DateTime.UtcNow.AddMinutes(
            _jwtSettings.ExpirationMinutes);

        var token = GenerateToken(user, expiresAt);

        return new LoginResponse
        {
            AccessToken = token,
            ExpiresAt = expiresAt,
            User = new LoginUserResponse
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role?.Name ?? "User"
            }
        };
    }

    private string GenerateToken(User user, DateTime expiresAt)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role?.Name ?? "User"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_jwtSettings.Key));

        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

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

    // 1. Luồng tiếp nhận đăng ký: Báo lỗi ngay lập tức nếu email đã tồn tại
    public async Task<string> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var cleanEmail = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var cleanFullName = request.FullName?.Trim() ?? string.Empty;

        if (cleanFullName.Length < 2 || cleanFullName.Length > 100)
        {
            throw new Exception("Full name must be between 2 and 100 characters.");
        }

        if (string.IsNullOrWhiteSpace(cleanEmail) || !EmailFormatRegex.IsMatch(cleanEmail) || cleanEmail.Contains(".."))
        {
            throw new Exception("Invalid email address format.");
        }

        if (string.IsNullOrWhiteSpace(request.Password) || !StrongPasswordRegex.IsMatch(request.Password))
        {
            throw new Exception("Password must be at least 8 characters long and contain at least one uppercase letter, one lowercase letter, one number, and one special character.");
        }

        // KIỂM TRA TRỰC TIẾP DATABASE: Nếu email đã có thì chặn ngay lập tức
        var emailExists = await _context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email.ToLower() == cleanEmail, cancellationToken);

        if (emailExists)
        {
            throw new Exception("This email is already registered.");
        }

        // Chống spam gửi mail liên tục (60 giây)
        var rateLimitKey = $"RATE_LIMIT_{cleanEmail}";
        if (_cache.TryGetValue(rateLimitKey, out _))
        {
            throw new Exception("Please wait 60 seconds before requesting a new code.");
        }

        var verificationCode = Generate5CharacterCode();

        Console.WriteLine($"\n==========================================");
        Console.WriteLine($"[OTP CODE]: {verificationCode} for {cleanEmail}");
        Console.WriteLine($"==========================================\n");

        var cacheKey = $"REG_OTP_{cleanEmail}";
        var cacheEntry = new PendingRegistration
        {
            Email = cleanEmail,
            Password = request.Password,
            FullName = cleanFullName,
            Code = verificationCode,
            FailedAttempts = 0
        };

        _cache.Set(cacheKey, cacheEntry, TimeSpan.FromMinutes(10));
        _cache.Set(rateLimitKey, true, TimeSpan.FromSeconds(60));

        var emailBody = $@"
            <div style='font-family: Arial, sans-serif; padding: 20px; line-height: 1.6;'>
                <h2 style='color: #1a73e8;'>DroneOps Verification Code</h2>
                <p>Hello <b>{cacheEntry.FullName}</b>,</p>
                <p>Your 5-character verification code is:</p>
                <div style='background-color: #f1f3f4; padding: 12px 20px; border-radius: 8px; width: fit-content; margin: 16px 0;'>
                    <span style='font-size: 28px; font-weight: bold; letter-spacing: 6px; color: #1a73e8;'>{verificationCode}</span>
                </div>
                <p style='color: #5f6368; font-size: 13px;'>This code will expire in 10 minutes. Please do not share it with anyone.</p>
            </div>";

        await _emailService.SendEmailAsync(
            cleanEmail,
            "DroneOps - Verification Code",
            emailBody);

        return "Verification code has been sent to your email.";
    }

    // 2. Luồng xác thực mã OTP và lưu tài khoản vào Database
    public async Task<bool> VerifyRegisterAsync(
        VerifyRegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var cleanEmail = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var cleanCode = request.Code?.Trim() ?? string.Empty;

        var cacheKey = $"REG_OTP_{cleanEmail}";

        if (!_cache.TryGetValue(cacheKey, out PendingRegistration? pending) || pending == null)
        {
            throw new Exception("Verification code has expired or does not exist. Please register again.");
        }

        if (!string.Equals(pending.Code, cleanCode, StringComparison.OrdinalIgnoreCase))
        {
            pending.FailedAttempts++;
            if (pending.FailedAttempts >= 5)
            {
                _cache.Remove(cacheKey);
                throw new Exception("Too many incorrect attempts. Your code has been invalidated. Please register again.");
            }

            _cache.Set(cacheKey, pending, TimeSpan.FromMinutes(10));
            throw new Exception($"Invalid verification code. You have {5 - pending.FailedAttempts} attempt(s) remaining.");
        }

        // Lấy Role có sẵn trong DB (không tự ý INSERT vào bảng Role tránh xung đột)
        var defaultRole = await _context.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        Guid roleId;
        if (defaultRole != null)
        {
            roleId = defaultRole.Id;
        }
        else
        {
            // Nếu bảng Role rỗng, tạo Role mới với ID cố định an toàn
            var newRole = new Role
            {
                Id = Guid.NewGuid(),
                Name = "Pilot",
                CreatedAt = DateTimeOffset.UtcNow
            };
            await _context.Roles.AddAsync(newRole, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            roleId = newRole.Id;
        }

        var newUser = new User
        {
            Id = Guid.NewGuid(),
            Email = pending.Email,
            PasswordHash = BC.HashPassword(pending.Password),
            FullName = pending.FullName,
            RoleId = roleId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            await _context.Users.AddAsync(newUser, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _cache.Remove(cacheKey);
            if (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
            {
                throw new Exception("This email is already registered.");
            }
            throw new Exception($"Database error: {ex.InnerException?.Message ?? ex.Message}");
        }

        _cache.Remove(cacheKey);
        return true;
    }

    private class PendingRegistration
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public int FailedAttempts { get; set; }
    }
}