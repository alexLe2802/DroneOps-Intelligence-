using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace DroneOps.API.Auth;

public sealed class FirebaseIdentityProvider(IOptions<AuthSettings> settings, IWebHostEnvironment environment,
    ILogger<FirebaseIdentityProvider> logger) : IFirebaseIdentityProvider, IFirebaseAccountDirectory, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private FirebaseApp? app;
    private int disposed;
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
            // This is a freshly issued interactive-login token. The application
            // session created below is independently revocable in Postgres, so a
            // second Firebase revocation round trip here only delays sign-in.
            var token = await auth.VerifyIdTokenAsync(idToken, false, ct);
            return FirebaseLoginPolicy.Validate(token.Uid, token.Claims, DateTimeOffset.UtcNow);
        }
        catch (IdentityRejectedException e)
        {
            logger.LogWarning("Firebase login policy rejected a token: {Reason}", e.Reason);
            throw;
        }
        catch (FirebaseAuthException e)
        {
            logger.LogWarning("Firebase rejected an ID token: {AuthErrorCode}", e.AuthErrorCode);
            throw new IdentityRejectedException("firebase_id_token_rejected");
        }
        catch (ArgumentException)
        {
            logger.LogWarning("Firebase ID token was malformed.");
            throw new IdentityRejectedException("malformed_id_token");
        }
        catch (HttpRequestException) { throw new IdentityUnavailableException(); }
    }

    public async Task<string> CreateSessionAsync(string idToken, TimeSpan duration, CancellationToken ct)
    {
        try
        {
            return await (await GetAuthAsync(ct)).CreateSessionCookieAsync(idToken, new SessionCookieOptions { ExpiresIn = duration }, ct);
        }
        catch (FirebaseAuthException e)
        {
            logger.LogWarning("Firebase could not create a session cookie: {AuthErrorCode}", e.AuthErrorCode);
            throw new IdentityRejectedException("session_cookie_rejected");
        }
        catch (HttpRequestException) { throw new IdentityUnavailableException(); }
    }

    public async Task<string> VerifySessionAsync(string cookie, CancellationToken ct)
    {
        // Postgres is the live source of truth for session revocation and account
        // status on every request. Verify Firebase's signature/expiry locally and
        // avoid a remote revocation lookup on every page and API request.
        try { return (await (await GetAuthAsync(ct)).VerifySessionCookieAsync(cookie, false, ct)).Uid; }
        catch (FirebaseAuthException) { throw new IdentityRejectedException("session_cookie_rejected"); }
        catch (ArgumentException) { throw new IdentityRejectedException("malformed_session_cookie"); }
        catch (HttpRequestException) { throw new IdentityUnavailableException(); }
    }

    public async Task<FirebaseAccount> CreateAsync(string email, string displayName, string password, CancellationToken ct)
    {
        try
        {
            var user = await (await GetAuthAsync(ct)).CreateUserAsync(new UserRecordArgs
            {
                Email = email, DisplayName = displayName, Password = password,
                EmailVerified = false, Disabled = false,
            }, ct);
            return new(user.Uid, user.Disabled);
        }
        catch (FirebaseAuthException e) when (e.AuthErrorCode == AuthErrorCode.EmailAlreadyExists)
        {
            throw new AccountProvisioningException("firebase_account_exists", "This email already exists in Firebase. Choose an existing Firebase account to grant Pilot access; its password will not be changed.");
        }
        catch (ArgumentException) { throw new AccountProvisioningException("invalid_account", "Firebase rejected the account details. Check the email and password requirements."); }
        catch (FirebaseAuthException) { throw new IdentityUnavailableException(); }
        catch (HttpRequestException) { throw new IdentityUnavailableException(); }
    }

    public async Task<FirebaseAccount> FindByEmailAsync(string email, CancellationToken ct)
    {
        try
        {
            var user = await (await GetAuthAsync(ct)).GetUserByEmailAsync(email, ct);
            return new(user.Uid, user.Disabled);
        }
        catch (FirebaseAuthException e) when (e.AuthErrorCode == AuthErrorCode.UserNotFound)
        {
            throw new AccountProvisioningException("firebase_account_missing", "No Firebase account exists for this email. Choose a new email/password account instead.");
        }
        catch (ArgumentException) { throw new AccountProvisioningException("invalid_account", "Enter a valid email address."); }
        catch (FirebaseAuthException) { throw new IdentityUnavailableException(); }
        catch (HttpRequestException) { throw new IdentityUnavailableException(); }
    }

    public void Dispose()
    {
        // The same singleton is exposed through both identity and account-management interfaces.
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        app?.Delete(); gate.Dispose();
    }
}
