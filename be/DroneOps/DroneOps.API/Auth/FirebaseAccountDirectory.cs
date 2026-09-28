namespace DroneOps.API.Auth;

public sealed record FirebaseAccount(string Uid, bool Disabled);

public interface IFirebaseAccountDirectory
{
    Task<FirebaseAccount> CreateAsync(string email, string displayName, string password, CancellationToken ct);
    Task<FirebaseAccount> FindByEmailAsync(string email, CancellationToken ct);
}

// Only controlled messages may cross the API boundary; never forward Firebase response bodies.
public sealed class AccountProvisioningException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
