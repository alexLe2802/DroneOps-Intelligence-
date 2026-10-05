using System.Security.Claims;
using System.Text.Encodings.Web;
using DroneOps.Persistence.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DroneOps.API.Auth;

public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IFirebaseIdentityProvider identity, IAuthStore store, SessionCookies cookies)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "FirebaseSession";
    private void SetDevelopmentAuthState(string state)
    {
        if (Context.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
            Response.Headers["X-DroneOps-Auth-State"] = state;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(cookies.Name, out var jwt))
        {
            SetDevelopmentAuthState("cookie_missing");
            return AuthenticateResult.NoResult();
        }
        if (jwt.Length > 8192)
        {
            SetDevelopmentAuthState("cookie_oversized");
            Logger.LogWarning("Session cookie exceeded the accepted size: {CookieLength} bytes.", jwt.Length);
            cookies.Clear(Response);
            return AuthenticateResult.Fail("Invalid session.");
        }
        try
        {
            var uid = await identity.VerifySessionAsync(jwt, Context.RequestAborted);
            var session = await store.FindSessionAsync(uid, SessionCookies.Hash(jwt), Context.RequestAborted);
            if (session is null || !AccountRoles.IsValid(session.Account.Role))
            {
                SetDevelopmentAuthState("application_session_missing");
                Logger.LogWarning("A valid Firebase session cookie had no active matching application session.");
                cookies.Clear(Response);
                return AuthenticateResult.Fail("Session expired or revoked.");
            }
            Context.Items[typeof(SessionIdentity)] = session;
            SetDevelopmentAuthState("authenticated");
            var claims = new[] {
                new Claim(ClaimTypes.NameIdentifier, session.Account.Id.ToString()),
                new Claim(ClaimTypes.Email, session.Account.Email),
                new Claim(ClaimTypes.Name, session.Account.DisplayName),
                new Claim(ClaimTypes.Role, session.Account.Role),
                new Claim("session_id", session.SessionId.ToString())
            };
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName));
        }
        catch (IdentityRejectedException exception)
        {
            SetDevelopmentAuthState("firebase_session_rejected");
            Logger.LogWarning("Firebase rejected a session cookie: {Reason}.", exception.Reason);
            cookies.Clear(Response);
            return AuthenticateResult.Fail("Invalid or revoked session.");
        }
    }
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Response.WriteAsJsonAsync(new { code = "session_expired", message = "Your session has expired. Sign in again." });
    }
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Response.WriteAsJsonAsync(new { code = "access_denied", message = "You do not have permission for this action." });
    }
}
