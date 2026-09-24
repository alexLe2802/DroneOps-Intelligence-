using System.Threading.RateLimiting;
using DroneOps.Persistence.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DroneOps.API.Auth;

public static class AuthSetup
{
    public static IServiceCollection AddDroneOpsAuth(this IServiceCollection services, IConfiguration config, IWebHostEnvironment environment)
    {
        services.AddOptions<AuthSettings>().Bind(config.GetSection("Auth"))
            .Validate(s => s.SessionHours is >= 1 and <= 336, "SessionHours must be between 1 and 336.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ProjectId), "Firebase project ID is required.")
            .Validate(s => Uri.TryCreate(s.PublicOrigin, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "https" || environment.IsDevelopment() && uri.IsLoopback) &&
                s.PublicOrigin == uri.GetLeftPart(UriPartial.Authority), "PublicOrigin must be an exact HTTPS origin (HTTP localhost allowed in development).")
            .ValidateOnStart();
        services.AddSingleton<SessionCookies>();
        services.AddSingleton<IFirebaseIdentityProvider, FirebaseIdentityProvider>();
        services.AddSingleton<IAuthStore, PostgresAuthStore>();
        var protection = services.AddDataProtection().SetApplicationName("DroneOps");
        if (config["Auth:DataProtectionKeyPath"] is { Length: > 0 } path)
            protection.PersistKeysToFileSystem(new DirectoryInfo(path));
        services.AddAntiforgery(o =>
        {
            o.HeaderName = "X-CSRF-TOKEN";
            o.Cookie.Name = environment.IsDevelopment() ? "droneops_csrf" : "__Host-droneops_csrf";
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.Path = "/";
        });
        services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, _ => { });
        services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = 429;
            o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return services;
    }

    public static void UseAuthErrorHandling(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            }
            try { await next(context); }
            catch (Exception e) when (e is IdentityUnavailableException or NpgsqlException or TimeoutException)
            {
                if (context.Response.HasStarted) throw;
                // Do not serialize/log tokens, credentials, SQL parameters, or upstream error bodies.
                context.Response.StatusCode = 503;
                await context.Response.WriteAsJsonAsync(new { code = "auth_unavailable", message = "Sign-in services are temporarily unavailable. Please try again later." });
            }
        });
    }

    public static void UseAuthCsrfProtection(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api") &&
                !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method))
            {
                var origin = context.RequestServices.GetRequiredService<IOptions<AuthSettings>>().Value.PublicOrigin;
                if (context.Request.Headers.Origin.ToString() != origin)
                {
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsJsonAsync(new { code = "invalid_origin", message = "Request origin is not allowed." });
                    return;
                }
                try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
                catch (AntiforgeryValidationException)
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsJsonAsync(new { code = "invalid_csrf", message = "Refresh the page and try again." });
                    return;
                }
            }
            await next(context);
        });
    }
}
