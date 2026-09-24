using System.Text.Json;

namespace DroneOps.API.Auth;

// Called only after Admin SDK signature, issuer, audience, expiration and revocation verification.
public static class FirebaseLoginPolicy
{
    public static FirebaseIdentity Validate(string uid, JsonElement claims, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(uid) ||
            !claims.TryGetProperty("email_verified", out var verified) || verified.ValueKind != JsonValueKind.True ||
            !claims.TryGetProperty("email", out var email) || email.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(email.GetString()) ||
            !claims.TryGetProperty("firebase", out var firebase) || firebase.ValueKind != JsonValueKind.Object ||
            !firebase.TryGetProperty("sign_in_provider", out var provider) || provider.ValueKind != JsonValueKind.String ||
            provider.GetString() is not ("google.com" or "password") ||
            !claims.TryGetProperty("auth_time", out var authTime) || authTime.ValueKind != JsonValueKind.Number || !authTime.TryGetInt64(out var seconds))
            throw new IdentityRejectedException();
        var age = now.ToUnixTimeSeconds() - seconds;
        if (age < -30 || age > 300) throw new IdentityRejectedException();
        return new(uid, email.GetString()!.Trim().ToLowerInvariant());
    }
}
