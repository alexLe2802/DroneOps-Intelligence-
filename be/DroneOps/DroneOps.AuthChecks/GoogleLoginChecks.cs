using System.Reflection;
using DroneOps.API.Controllers;
using DroneOps.Application.DTOs.Request.Auth;
using DroneOps.Application.DTOs.Response.Auth;
using DroneOps.Application.Interfaces.Auth;
using DroneOps.Application.Services.Auth;
using DroneOps.Application.Settings;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DroneOps.AuthChecks;

public static class GoogleLoginChecks
{
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }

    public static async Task<int> RunAsync()
    {
        var count = 0;

        // --- 1. Reflection / API contract checks ---
        var method = typeof(AuthController).GetMethod("GoogleLogin");
        Check(method != null, "AuthController has GoogleLogin action");
        count++;

        Check(method!.GetCustomAttribute<AllowAnonymousAttribute>() != null, "Google login allows anonymous access");
        count++;

        var httpPost = method!.GetCustomAttribute<HttpPostAttribute>();
        Check(httpPost?.Template == "google-login", "Google login endpoint template is google-login");
        count++;

        var idTokenProp = typeof(GoogleLoginRequest).GetProperty("IdToken");
        Check(idTokenProp != null && idTokenProp.PropertyType == typeof(string), "GoogleLoginRequest has IdToken property");
        count++;

        Check(GoogleAuthSettings.SectionName == "GoogleAuth", "GoogleAuthSettings section is GoogleAuth");
        count++;

        // --- 2. Controller action response mappings ---
        var fakeAuthService = new MockAuthService();
        var controller = new AuthController(fakeAuthService);

        // 2a. Empty IdToken -> 400 Bad Request
        var badReq = await controller.GoogleLogin(new GoogleLoginRequest { IdToken = "" }, CancellationToken.None);
        Check(badReq is BadRequestObjectResult, "Empty IdToken produces 400 BadRequest");
        count++;

        // 2b. Missing ClientId (InvalidOperationException) -> 503 Service Unavailable
        fakeAuthService.Mode = MockAuthMode.MissingConfig;
        var unavailable = await controller.GoogleLogin(new GoogleLoginRequest { IdToken = "valid-token" }, CancellationToken.None);
        Check(unavailable is ObjectResult obj && obj.StatusCode == StatusCodes.Status503ServiceUnavailable,
            "Missing Google ClientId returns 503 Service Unavailable");
        count++;

        // 2c. Invalid / Expired Token (UnauthorizedAccessException) -> 401 Unauthorized
        fakeAuthService.Mode = MockAuthMode.InvalidToken;
        var unauth = await controller.GoogleLogin(new GoogleLoginRequest { IdToken = "bad-token" }, CancellationToken.None);
        Check(unauth is UnauthorizedObjectResult, "Invalid or expired token returns 401 Unauthorized");
        count++;

        // 2d. Email not found in DB (null result) -> 401 Unauthorized
        fakeAuthService.Mode = MockAuthMode.UserNotFound;
        var notFound = await controller.GoogleLogin(new GoogleLoginRequest { IdToken = "valid-token" }, CancellationToken.None);
        Check(notFound is UnauthorizedObjectResult uor && uor.Value?.ToString()?.Contains("No account found") == true,
            "Email not found in DB returns 401 Unauthorized with clear message");
        count++;

        // 2e. RequiresFirstLogin -> 200 OK with first_login_required
        fakeAuthService.Mode = MockAuthMode.FirstLoginRequired;
        var firstLogin = await controller.GoogleLogin(new GoogleLoginRequest { IdToken = "valid-token" }, CancellationToken.None);
        Check(firstLogin is OkObjectResult okFirst && okFirst.Value?.ToString()?.Contains("first_login_required") == true,
            "RequiresFirstLogin returns 200 OK with first_login_required payload");
        count++;

        // 2f. Successful login -> 200 OK with LoginResponse
        fakeAuthService.Mode = MockAuthMode.Success;
        var successResult = await controller.GoogleLogin(new GoogleLoginRequest { IdToken = "valid-token" }, CancellationToken.None);
        Check(successResult is OkObjectResult okSuccess && okSuccess.Value is LoginResponse resp && !string.IsNullOrEmpty(resp.AccessToken),
            "Valid Google login returns 200 OK with valid LoginResponse");
        count++;

        // --- 3. Offline AuthService.GoogleLoginAsync Unit Tests ---
        var jwtOptions = Options.Create(new JwtSettings
        {
            Key = "Offline-Test-Secret-Key-Must-Be-At-Least-32-Chars-Long!",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            ExpirationMinutes = 60
        });

        var fakeRepo = new FakeUserRepository();
        var activeUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "active.pilot@example.com",
            FullName = "Active Pilot",
            IsActive = true,
            RequiresFirstLogin = false,
            Role = new Role { Name = "uav_operator" }
        };
        var inactiveUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "inactive.user@example.com",
            FullName = "Inactive User",
            IsActive = false,
            RequiresFirstLogin = false,
            Role = new Role { Name = "uav_operator" }
        };
        var firstLoginUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "firstlogin.pilot@example.com",
            FullName = "New Pilot",
            IsActive = true,
            RequiresFirstLogin = true,
            Role = new Role { Name = "uav_operator" }
        };

        fakeRepo.Users.Add(activeUser.Email, activeUser);
        fakeRepo.Users.Add(inactiveUser.Email, inactiveUser);
        fakeRepo.Users.Add(firstLoginUser.Email, firstLoginUser);

        // 3a. Throws when GoogleAuth:ClientId is empty
        var emptyConfigAuthService = new AuthService(
            userRepository: fakeRepo,
            context: null!,
            emailService: null!,
            cache: new MemoryCache(new MemoryCacheOptions()),
            jwtSettings: jwtOptions,
            googleAuthSettings: Options.Create(new GoogleAuthSettings { ClientId = "" }),
            googleTokenValidator: (_, _) => Task.FromResult(new GoogleJsonWebSignature.Payload())
        );

        var caughtInvalidOp = false;
        try
        {
            await emptyConfigAuthService.GoogleLoginAsync(new GoogleLoginRequest { IdToken = "token" });
        }
        catch (InvalidOperationException)
        {
            caughtInvalidOp = true;
        }
        Check(caughtInvalidOp, "AuthService throws InvalidOperationException when ClientId is unconfigured");
        count++;

        // Setup test AuthService with valid ClientId and mock offline validator
        var validConfig = Options.Create(new GoogleAuthSettings { ClientId = "test-client-id.apps.googleusercontent.com" });
        GoogleJsonWebSignature.Payload? currentMockPayload = null;
        Exception? currentValidatorException = null;

        var authService = new AuthService(
            userRepository: fakeRepo,
            context: null!,
            emailService: null!,
            cache: new MemoryCache(new MemoryCacheOptions()),
            jwtSettings: jwtOptions,
            googleAuthSettings: validConfig,
            googleTokenValidator: (token, settings) =>
            {
                if (currentValidatorException != null) throw currentValidatorException;
                return Task.FromResult(currentMockPayload ?? new GoogleJsonWebSignature.Payload());
            }
        );

        // 3b. Invalid JWT exception from Google validator
        currentValidatorException = new InvalidJwtException("Signature verification failed.");
        var caughtInvalidJwt = false;
        try
        {
            await authService.GoogleLoginAsync(new GoogleLoginRequest { IdToken = "invalid-token" });
        }
        catch (UnauthorizedAccessException ex) when (ex.Message.Contains("Invalid or expired"))
        {
            caughtInvalidJwt = true;
        }
        Check(caughtInvalidJwt, "AuthService translates InvalidJwtException to UnauthorizedAccessException");
        count++;

        // 3c. Unverified email in payload
        currentValidatorException = null;
        currentMockPayload = new GoogleJsonWebSignature.Payload
        {
            Email = "active.pilot@example.com",
            EmailVerified = false
        };
        var caughtUnverified = false;
        try
        {
            await authService.GoogleLoginAsync(new GoogleLoginRequest { IdToken = "token" });
        }
        catch (UnauthorizedAccessException ex) when (ex.Message.Contains("not verified"))
        {
            caughtUnverified = true;
        }
        Check(caughtUnverified, "AuthService rejects unverified Google email");
        count++;

        // 3d. User not in DB returns null
        currentMockPayload = new GoogleJsonWebSignature.Payload
        {
            Email = "notfound@example.com",
            EmailVerified = true
        };
        var nullResult = await authService.GoogleLoginAsync(new GoogleLoginRequest { IdToken = "token" });
        Check(nullResult == null, "AuthService returns null when user does not exist in DB");
        count++;

        // 3e. Inactive user throws UnauthorizedAccessException
        currentMockPayload = new GoogleJsonWebSignature.Payload
        {
            Email = "inactive.user@example.com",
            EmailVerified = true
        };
        var caughtInactive = false;
        try
        {
            await authService.GoogleLoginAsync(new GoogleLoginRequest { IdToken = "token" });
        }
        catch (UnauthorizedAccessException ex) when (ex.Message.Contains("deactivated"))
        {
            caughtInactive = true;
        }
        Check(caughtInactive, "AuthService rejects deactivated user with UnauthorizedAccessException");
        count++;

        // 3f. Pilot requiring first login returns RequiresFirstLogin = true
        currentMockPayload = new GoogleJsonWebSignature.Payload
        {
            Email = "firstlogin.pilot@example.com",
            EmailVerified = true
        };
        var firstLoginResp = await authService.GoogleLoginAsync(new GoogleLoginRequest { IdToken = "token" });
        Check(firstLoginResp != null && firstLoginResp.RequiresFirstLogin, "AuthService returns RequiresFirstLogin for pilot pending first password set");
        count++;

        // 3g. Active user returns valid JWT token and normalized role
        currentMockPayload = new GoogleJsonWebSignature.Payload
        {
            Email = "active.pilot@example.com",
            EmailVerified = true
        };
        var successResp = await authService.GoogleLoginAsync(new GoogleLoginRequest { IdToken = "token" });
        Check(successResp != null && !string.IsNullOrEmpty(successResp.AccessToken) && successResp.User?.Role == "uav_operator",
            "AuthService generates valid JWT and user info for active pilot");
        count++;

        // 3h. Case-insensitive email lookup verification
        currentMockPayload = new GoogleJsonWebSignature.Payload
        {
            Email = "ACTIVE.PILOT@EXAMPLE.COM",
            EmailVerified = true
        };
        var caseInsensitiveResp = await authService.GoogleLoginAsync(new GoogleLoginRequest { IdToken = "token" });
        Check(caseInsensitiveResp != null && !string.IsNullOrEmpty(caseInsensitiveResp.AccessToken),
            "AuthService handles case-insensitive Google email matching");
        count++;

        return count;
    }

    private enum MockAuthMode
    {
        Success,
        MissingConfig,
        InvalidToken,
        UserNotFound,
        FirstLoginRequired
    }

    private class MockAuthService : IAuthService
    {
        public MockAuthMode Mode { get; set; } = MockAuthMode.Success;

        public Task<LoginResponse?> GoogleLoginAsync(GoogleLoginRequest request, CancellationToken cancellationToken = default)
        {
            switch (Mode)
            {
                case MockAuthMode.MissingConfig:
                    throw new InvalidOperationException("Google authentication is not configured.");
                case MockAuthMode.InvalidToken:
                    throw new UnauthorizedAccessException("Invalid or expired Google token.");
                case MockAuthMode.UserNotFound:
                    return Task.FromResult<LoginResponse?>(null);
                case MockAuthMode.FirstLoginRequired:
                    return Task.FromResult<LoginResponse?>(new LoginResponse { RequiresFirstLogin = true });
                case MockAuthMode.Success:
                default:
                    return Task.FromResult<LoginResponse?>(new LoginResponse
                    {
                        AccessToken = "fake-jwt-token",
                        ExpiresAt = DateTime.UtcNow.AddHours(1),
                        User = new LoginUserResponse
                        {
                            Id = Guid.NewGuid(),
                            FullName = "Mock User",
                            Email = "mock@example.com",
                            Role = "uav_operator"
                        }
                    });
            }
        }

        public Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<string> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<string> RegisterPilotAsync(RegisterRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> VerifyRegisterAsync(VerifyRegisterRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private class FakeUserRepository : IUserRepository
    {
        public Dictionary<string, User> Users { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<User?> GetByEmailWithRoleAsync(string email, CancellationToken cancellationToken = default)
        {
            Users.TryGetValue(email, out var user);
            return Task.FromResult(user);
        }

        public Task<User?> GetByIdWithRoleAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> EmailExistsAsync(string email, Guid? excludingUserId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedWithRoleAsync(int pageNumber, int pageSize, string? search, string? role, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<User?> FirstOrDefaultAsync(System.Linq.Expressions.Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<User>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task AddAsync(User entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Update(User entity) { }
        public void Remove(User entity) { }
        public Task<bool> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
