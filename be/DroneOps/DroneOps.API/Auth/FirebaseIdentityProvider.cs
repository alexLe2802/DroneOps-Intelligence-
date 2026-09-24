using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DroneOps.API.Auth;

public sealed class FirebaseIdentityProvider(IOptions<AuthSettings> settings, IWebHostEnvironment environment) : IFirebaseIdentityProvider, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private FirebaseApp? app;
    private async Task<FirebaseAuth> GetAuthAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (app is null)
            {
                // An optional local path contains no secret itself; credentials remain server-only.
                GoogleCredential credential;
                if (!string.IsNullOrWhiteSpace(settings.Value.ServiceAccountPath))
                {
                    var serviceAccount = await CredentialFactory.FromFileAsync<ServiceAccountCredential>(
                        Path.GetFullPath(settings.Value.ServiceAccountPath, environment.ContentRootPath), ct);
                    if (serviceAccount.ProjectId != settings.Value.ProjectId)
                        throw new IdentityUnavailableException();
                    credential = serviceAccount.ToGoogleCredential();
                }
                else
                {
                    credential = await GoogleCredential.GetApplicationDefaultAsync(ct);
                }
                app = FirebaseApp.Create(new AppOptions { ProjectId = settings.Value.ProjectId, Credential = credential }, "DroneOps");
            }
            return FirebaseAuth.GetAuth(app);
        }
        catch (Exception e) when (e is not OperationCanceledException) { throw new IdentityUnavailableException(); }
        finally { gate.Release(); }
    }

    public async Task<FirebaseIdentity> VerifyLoginAsync(string idToken, CancellationToken ct)
    {
        try
        {
            var auth = await GetAuthAsync(ct);
            var token = await auth.VerifyIdTokenAsync(idToken, true, ct);
            return FirebaseLoginPolicy.Validate(token.Uid, JsonSerializer.SerializeToElement(token.Claims), DateTimeOffset.UtcNow);
        }
        catch (FirebaseAuthException) { throw new IdentityRejectedException(); }
        catch (ArgumentException) { throw new IdentityRejectedException(); }
        catch (HttpRequestException) { throw new IdentityUnavailableException(); }
    }

    public async Task<string> CreateSessionAsync(string idToken, TimeSpan duration, CancellationToken ct)
    {
        try
        {
            return await (await GetAuthAsync(ct)).CreateSessionCookieAsync(idToken, new SessionCookieOptions { ExpiresIn = duration }, ct);
        }
        catch (FirebaseAuthException) { throw new IdentityRejectedException(); }
        catch (HttpRequestException) { throw new IdentityUnavailableException(); }
    }

    public async Task<string> VerifySessionAsync(string cookie, CancellationToken ct)
    {
        try { return (await (await GetAuthAsync(ct)).VerifySessionCookieAsync(cookie, true, ct)).Uid; }
        catch (FirebaseAuthException) { throw new IdentityRejectedException(); }
        catch (ArgumentException) { throw new IdentityRejectedException(); }
        catch (HttpRequestException) { throw new IdentityUnavailableException(); }
    }

    public void Dispose() { app?.Delete(); gate.Dispose(); }
}
