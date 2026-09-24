using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DroneOps.API.Auth;
using DroneOps.Persistence.Auth;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Integration checks use the real middleware/controllers with fake identity/database boundaries.
// No Firebase credentials, production users, or remote database are touched.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:PublicOrigin"] = "http://localhost:3000" });
builder.Services.AddDroneOpsAuth(builder.Configuration, builder.Environment);
builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
var store = new FakeStore();
builder.Services.Replace(ServiceDescriptor.Singleton<IAuthStore>(store));
builder.Services.Replace(ServiceDescriptor.Singleton<IFirebaseIdentityProvider, FakeIdentity>());
var app = builder.Build();
app.UseAuthErrorHandling(); app.UseRouting(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthCsrfProtection(); app.UseAuthorization(); app.MapControllers();
await app.StartAsync();
var origin = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
HttpClient Client() => new(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = new Uri(origin) };
async Task<HttpResponseMessage> Mutate(HttpClient client, string path, object? data = null, string method = "POST", string requestOrigin = "http://localhost:3000", bool csrf = true)
{
    var req = new HttpRequestMessage(new HttpMethod(method), path);
    req.Headers.Add("Origin", requestOrigin);
    if (csrf)
    {
        var tokens = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        req.Headers.Add("X-CSRF-TOKEN", tokens.GetProperty("csrfToken").GetString());
    }
    if (data is not null) req.Content = JsonContent.Create(data);
    return await client.SendAsync(req);
}
try
{
    var now = DateTimeOffset.UtcNow;
    JsonElement Claims(string provider, bool verified = true, long? authTime = null) => JsonSerializer.SerializeToElement(new {
        email = "Manager@Example.com", email_verified = verified,
        firebase = new { sign_in_provider = provider }, auth_time = authTime ?? now.ToUnixTimeSeconds()
    });
    bool Rejected(JsonElement claims) {
        try { FirebaseLoginPolicy.Validate("uid", claims, now); return false; }
        catch (IdentityRejectedException) { return true; }
    }
    Check(FirebaseLoginPolicy.Validate("uid", Claims("password"), now).Email == "manager@example.com", "Verified password identity accepted and email normalized");
    Check(FirebaseLoginPolicy.Validate("uid", Claims("google.com"), now).Uid == "uid", "Verified Google identity still accepted");
    Check(Rejected(Claims("password", false)), "Unverified password account rejected on server");
    Check(Rejected(Claims("google.com", false)), "Unverified Google account rejected on server");
    Check(Rejected(Claims("custom")), "Custom-token sign-in cannot bypass provider restriction");
    Check(Rejected(Claims("anonymous")), "Anonymous provider rejected");
    Check(Rejected(Claims("password", authTime: now.ToUnixTimeSeconds() - 301)), "Stale password sign-in rejected");
    Check(Rejected(Claims("password", authTime: now.ToUnixTimeSeconds() + 31)), "Future authentication timestamp rejected");
    Check(Rejected(JsonSerializer.SerializeToElement(new { email = "manager@example.com" })), "Missing provider and verification claims rejected");
    using var anon = Client();
    Check((await anon.GetAsync("/api/auth/me")).StatusCode == HttpStatusCode.Unauthorized, "Anonymous user cannot access protected data");
    Check((await Mutate(anon, "/api/auth/session", new { idToken = "valid-operator-proof-0001" }, csrf: false)).StatusCode == HttpStatusCode.BadRequest, "Login requires CSRF token");
    Check((await Mutate(anon, "/api/auth/session", new { idToken = "valid-operator-proof-0001" }, requestOrigin: "https://evil.example")).StatusCode == HttpStatusCode.Forbidden, "Cross-origin login rejected even with CSRF token");
    Check((await Mutate(anon, "/api/auth/session", new { idToken = "invalid-token-000000000000" })).StatusCode == HttpStatusCode.Unauthorized, "Invalid identity proof rejected");
    Check((await Mutate(anon, "/api/auth/session", new { idToken = "valid-unknown-proof-0001" })).StatusCode == HttpStatusCode.Forbidden, "Unprovisioned Google account rejected");
    using var op = Client();
    var login = await Mutate(op, "/api/auth/session", new { idToken = "valid-operator-proof-0001", role = "operations_manager" });
    Check(login.StatusCode == HttpStatusCode.OK, "Provisioned operator signs in");
    var setCookie = login.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("droneops_session="));
    Check(setCookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) && setCookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase), "Session cookie is HttpOnly and SameSite Strict");
    var rawCookie = setCookie.Split(';')[0].Split('=', 2)[1];
    var body = await login.Content.ReadAsStringAsync();
    Check(!body.Contains(rawCookie) && !body.Contains("idToken") && body.Contains("uav_operator"), "Response omits tokens and ignores client role escalation");
    Check((await op.GetAsync("/api/accounts")).StatusCode == HttpStatusCode.Forbidden, "Operator cannot list managed accounts");
    Check((await Mutate(op, "/api/accounts", new { email = "x@example.com", displayName = "X" })).StatusCode == HttpStatusCode.Forbidden, "Operator cannot provision accounts");
    using var second = Client();
    Check((await Mutate(second, "/api/auth/session", new { idToken = "valid-operator-proof-0002" })).StatusCode == HttpStatusCode.OK, "Second browser has independent session");
    Check((await second.GetFromJsonAsync<JsonElement>("/api/auth/sessions")).GetArrayLength() == 2, "Both active sessions are listed");
    using var manager = Client();
    Check((await Mutate(manager, "/api/auth/session", new { idToken = "valid-manager-proof-00001" })).StatusCode == HttpStatusCode.OK, "Provisioned manager signs in");
    Check((await manager.GetAsync("/api/accounts")).StatusCode == HttpStatusCode.OK, "Manager can access account management");
    var managerSessions = await manager.GetFromJsonAsync<JsonElement>("/api/auth/sessions");
    var managerSid = managerSessions[0].GetProperty("id").GetGuid();
    await Mutate(op, $"/api/auth/sessions/{managerSid}", method: "DELETE");
    Check((await manager.GetAsync("/api/auth/me")).StatusCode == HttpStatusCode.OK, "A user cannot revoke another user's session");
    Check((await Mutate(manager, "/api/accounts", new { email = "new@example.com", displayName = "New Operator", role = "operations_manager" })).StatusCode == HttpStatusCode.Created && store.LastProvisionedRole == AccountRoles.Operator, "Manager provisioning cannot create arbitrary privileged roles");
    Check((await Mutate(manager, $"/api/accounts/{FakeStore.ManagerId}/access", new { isActive = false }, "PATCH")).StatusCode == HttpStatusCode.NotFound, "Manager role cannot be disabled through operator endpoint");
    await Mutate(op, "/api/auth/logout");
    Check((await op.GetAsync("/api/auth/me")).StatusCode == HttpStatusCode.Unauthorized, "Logout clears current authentication");
    using var replay = Client(); replay.DefaultRequestHeaders.Add("Cookie", "droneops_session=" + rawCookie);
    Check((await replay.GetAsync("/api/auth/me")).StatusCode == HttpStatusCode.Unauthorized, "Copied JWT cannot replay a revoked database session");
    Check((await second.GetAsync("/api/auth/me")).StatusCode == HttpStatusCode.OK, "Single logout preserves another browser session");
    using var repeated = Client();
    Check((await Mutate(repeated, "/api/auth/session", new { idToken = "valid-operator-proof-0001" })).StatusCode == HttpStatusCode.Forbidden, "Consumed login proof cannot recreate a revoked session");
    await Mutate(second, "/api/auth/logout-all");
    Check((await second.GetAsync("/api/auth/me")).StatusCode == HttpStatusCode.Unauthorized, "Logout everywhere revokes account sessions");
    // Database state is re-read on every request, rather than trusting the JWT's role/status.
    store.Disabled = true;
    Check((await manager.GetAsync("/api/auth/me")).StatusCode == HttpStatusCode.Unauthorized, "Disabled account loses existing access immediately");
    store.Disabled = false;
    using var expired = Client(); expired.DefaultRequestHeaders.Add("Cookie", "droneops_session=expired.jwt");
    Check((await expired.GetAsync("/api/auth/me")).StatusCode == HttpStatusCode.Unauthorized, "Expired JWT rejected before session lookup");
    var env = builder.Environment; env.EnvironmentName = "Production";
    var production = new SessionCookies(env);
    Check(production.Name.StartsWith("__Host-") && production.Options().Secure && production.Options().HttpOnly && production.Options().Domain is null && production.Options().Path == "/", "Production cookie uses secure host-only settings");
    Console.WriteLine($"\n{passed} auth integration checks passed.");
}
finally { await app.StopAsync(); await app.DisposeAsync(); }

sealed class FakeIdentity : IFirebaseIdentityProvider
{
    public Task<FirebaseIdentity> VerifyLoginAsync(string token, CancellationToken ct)
    {
        if (!token.StartsWith("valid-")) throw new IdentityRejectedException();
        var name = token.Split('-')[1]; return Task.FromResult(new FirebaseIdentity(name, name + "@example.com"));
    }
    public Task<string> CreateSessionAsync(string token, TimeSpan duration, CancellationToken ct) => Task.FromResult(token.Split('-')[1] + "." + Guid.NewGuid() + ".jwt");
    public Task<string> VerifySessionAsync(string cookie, CancellationToken ct)
    {
        if (cookie == "expired.jwt") throw new IdentityRejectedException();
        return Task.FromResult(cookie.Split('.')[0]);
    }
}
sealed class FakeStore : IAuthStore
{
    public static readonly Guid ManagerId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();
    public bool Disabled { get; set; }
    public string? LastProvisionedRole { get; set; }
    private readonly Dictionary<string, SessionIdentity> sessions = new();
    private readonly HashSet<string> proofs = [];
    public Task<SessionIdentity?> CreateSessionAsync(string uid, string email, string hash, string proof, DateTimeOffset expiry, string agent, CancellationToken ct)
    {
        if (uid == "unknown" || !proofs.Add(proof)) return Task.FromResult<SessionIdentity?>(null);
        var account = new Account(uid == "manager" ? ManagerId : OperatorId, email, uid, uid == "manager" ? AccountRoles.Manager : AccountRoles.Operator, true);
        var session = new SessionIdentity(account, Guid.NewGuid(), expiry); sessions[hash] = session;
        return Task.FromResult<SessionIdentity?>(session);
    }
    public Task<SessionIdentity?> FindSessionAsync(string uid, string hash, CancellationToken ct) => Task.FromResult(!Disabled && sessions.TryGetValue(hash, out var s) ? s : null);
    public Task<IReadOnlyList<SessionInfo>> ListSessionsAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<SessionInfo>>(sessions.Values.Where(s => s.Account.Id == id).Select(s => new SessionInfo(s.SessionId, DateTimeOffset.UtcNow, s.ExpiresAt, "Test browser")).ToArray());
    public Task RevokeSessionAsync(Guid account, Guid id, CancellationToken ct) { foreach (var key in sessions.Where(x => x.Value.Account.Id == account && x.Value.SessionId == id).Select(x => x.Key).ToArray()) sessions.Remove(key); return Task.CompletedTask; }
    public Task RevokeAllAsync(Guid account, CancellationToken ct) { foreach (var key in sessions.Where(x => x.Value.Account.Id == account).Select(x => x.Key).ToArray()) sessions.Remove(key); return Task.CompletedTask; }
    public Task<IReadOnlyList<Account>> ListAccountsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Account>>([]);
    public Task<bool> ProvisionOperatorAsync(string email, string name, Guid manager, CancellationToken ct) { LastProvisionedRole = AccountRoles.Operator; return Task.FromResult(true); }
    public Task<bool> SetOperatorAccessAsync(Guid account, bool active, Guid manager, CancellationToken ct) => Task.FromResult(account != ManagerId);
}
