using System.Security.Cryptography;
using System.Text;

namespace DroneOps.API.Auth;

public sealed class AuthSettings
{
    public string ProjectId { get; set; } = "droneops-intelligence";
    public string? ServiceAccountPath { get; set; }
    public string PublicOrigin { get; set; } = "http://localhost:3000";
    public string BootstrapManagerEmail { get; set; } = "impro2003@gmail.com";
    public int SessionHours { get; set; } = 8;
}

public sealed class SessionCookies(IWebHostEnvironment environment)
{
    public string Name => environment.IsDevelopment() ? "droneops_session" : "__Host-droneops_session";
    public CookieOptions Options(DateTimeOffset? expires = null) => new()
    {
        HttpOnly = true, Secure = !environment.IsDevelopment(), SameSite = SameSiteMode.Strict,
        Path = "/", IsEssential = true, Expires = expires
    };
    public void Clear(HttpResponse response) => response.Cookies.Delete(Name, Options());
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}

public sealed class IdentityRejectedException : Exception;
public sealed class IdentityUnavailableException : Exception;
public sealed record FirebaseIdentity(string Uid, string Email);

public interface IFirebaseIdentityProvider
{
    Task<FirebaseIdentity> VerifyLoginAsync(string idToken, CancellationToken ct);
    Task<string> CreateSessionAsync(string idToken, TimeSpan duration, CancellationToken ct);
    Task<string> VerifySessionAsync(string cookie, CancellationToken ct);
}
