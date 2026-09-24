namespace DroneOps.Persistence.Auth;

public static class AccountRoles
{
    public const string Operator = "uav_operator";
    public const string Manager = "operations_manager";
    public static bool IsValid(string role) => role is Operator or Manager;
}

public sealed record Account(Guid Id, string Email, string DisplayName, string Role, bool IsActive);
public sealed record SessionIdentity(Account Account, Guid SessionId, DateTimeOffset ExpiresAt);
public sealed record SessionInfo(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, string UserAgent);

public interface IAuthStore
{
    Task<SessionIdentity?> CreateSessionAsync(string uid, string email, string tokenHash, string loginProofHash,
        DateTimeOffset expiresAt, string userAgent, CancellationToken ct);
    Task<SessionIdentity?> FindSessionAsync(string uid, string tokenHash, CancellationToken ct);
    Task<IReadOnlyList<SessionInfo>> ListSessionsAsync(Guid accountId, CancellationToken ct);
    Task RevokeSessionAsync(Guid accountId, Guid sessionId, CancellationToken ct);
    Task RevokeAllAsync(Guid accountId, CancellationToken ct);
    Task<IReadOnlyList<Account>> ListAccountsAsync(CancellationToken ct);
    Task<bool> ProvisionOperatorAsync(string email, string displayName, Guid managerId, CancellationToken ct);
    Task<bool> SetOperatorAccessAsync(Guid accountId, bool active, Guid managerId, CancellationToken ct);
}
