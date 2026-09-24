using System.ComponentModel.DataAnnotations;
using DroneOps.Persistence.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace DroneOps.API.Auth;

[ApiController]
[Route("api/auth")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController(IFirebaseIdentityProvider identity, IAuthStore store,
    SessionCookies cookies, IOptions<AuthSettings> settings, IAntiforgery antiforgery) : ControllerBase
{
    private SessionIdentity Current => (SessionIdentity)HttpContext.Items[typeof(SessionIdentity)]!;

    [HttpGet("csrf"), AllowAnonymous]
    public IActionResult Csrf() => Ok(new { csrfToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [HttpPost("session"), HttpPost("google"), AllowAnonymous, EnableRateLimiting("login")]
    [RequestSizeLimit(16384)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        try
        {
            var verifiedIdentity = await identity.VerifyLoginAsync(request.IdToken, ct);
            var duration = TimeSpan.FromHours(settings.Value.SessionHours);
            var jwt = await identity.CreateSessionAsync(request.IdToken, duration, ct);
            var expires = DateTimeOffset.UtcNow.Add(duration);
            var session = await store.CreateSessionAsync(verifiedIdentity.Uid, verifiedIdentity.Email, SessionCookies.Hash(jwt),
                SessionCookies.Hash(request.IdToken), expires, Request.Headers.UserAgent.ToString(), ct);
            if (session is null) return StatusCode(403, new { code = "account_not_provisioned", message = "This account is not authorized. Contact your Operations Manager or sign in again." });
            // Rotate the previous application session only after the new login succeeds.
            if (HttpContext.Items[typeof(SessionIdentity)] is SessionIdentity previous)
                await store.RevokeSessionAsync(previous.Account.Id, previous.SessionId, ct);
            Response.Cookies.Append(cookies.Name, jwt, cookies.Options(expires));
            return Ok(new { account = session.Account, session.ExpiresAt });
        }
        catch (IdentityRejectedException) { return Unauthorized(new { code = "invalid_login", message = "Unable to verify sign-in. Use a verified email account. Please try again." }); }
    }

    [HttpGet("me")]
    public IActionResult Me() => Ok(new { account = Current.Account, Current.ExpiresAt });

    [HttpGet("sessions")]
    public async Task<IActionResult> Sessions(CancellationToken ct) => Ok((await store.ListSessionsAsync(Current.Account.Id, ct))
        .Select(s => new { s.Id, s.CreatedAt, s.ExpiresAt, s.UserAgent, isCurrent = s.Id == Current.SessionId }));

    [HttpDelete("sessions/{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        await store.RevokeSessionAsync(Current.Account.Id, id, ct);
        if (id == Current.SessionId) cookies.Clear(Response);
        return NoContent();
    }

    [HttpPost("logout"), AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (HttpContext.Items[typeof(SessionIdentity)] is SessionIdentity session)
            await store.RevokeSessionAsync(session.Account.Id, session.SessionId, ct);
        cookies.Clear(Response);
        return NoContent();
    }

    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        await store.RevokeAllAsync(Current.Account.Id, ct);
        cookies.Clear(Response);
        return NoContent();
    }
}

public sealed record LoginRequest([Required, StringLength(8192, MinimumLength = 20)] string IdToken);
