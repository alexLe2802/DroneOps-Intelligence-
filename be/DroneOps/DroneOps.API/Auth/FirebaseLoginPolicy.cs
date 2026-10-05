using System.Text.Json;
using Newtonsoft.Json.Linq;

namespace DroneOps.API.Auth;

// Called only after Admin SDK signature, issuer, audience, expiration and revocation verification.
public static class FirebaseLoginPolicy
{
    public static FirebaseIdentity Validate(string uid, IReadOnlyDictionary<string, object> claims, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(uid))
            throw new IdentityRejectedException("uid_missing");
        if (!claims.TryGetValue("email_verified", out var verifiedValue) || !ReadBoolean(verifiedValue))
            throw new IdentityRejectedException("email_not_verified");
        if (!claims.TryGetValue("email", out var emailValue) || ReadString(emailValue) is not { Length: > 0 } email)
            throw new IdentityRejectedException("email_missing_or_invalid");
        if (!claims.TryGetValue("firebase", out var firebaseValue) || ReadProvider(firebaseValue) is not { } provider)
            throw new IdentityRejectedException("provider_missing_or_invalid");
        if (provider is not ("google.com" or "password"))
            throw new IdentityRejectedException("provider_not_allowed");
        if (!claims.TryGetValue("auth_time", out var authTimeValue) || !ReadInt64(authTimeValue, out var seconds))
            throw new IdentityRejectedException("auth_time_missing_or_invalid");
        return ValidateAccepted(uid, email, seconds, now);
    }

    public static FirebaseIdentity Validate(string uid, JsonElement claims, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(uid))
            throw new IdentityRejectedException("uid_missing");
        if (!claims.TryGetProperty("email_verified", out var verified) || verified.ValueKind != JsonValueKind.True)
            throw new IdentityRejectedException($"email_not_verified_{verified.ValueKind}");
        if (!claims.TryGetProperty("email", out var email) || email.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(email.GetString()))
            throw new IdentityRejectedException($"email_missing_or_invalid_{email.ValueKind}");
        if (!claims.TryGetProperty("firebase", out var firebase) || firebase.ValueKind != JsonValueKind.Object)
            throw new IdentityRejectedException($"firebase_claim_missing_or_invalid_{firebase.ValueKind}");
        if (!firebase.TryGetProperty("sign_in_provider", out var provider) || provider.ValueKind != JsonValueKind.String)
            throw new IdentityRejectedException($"provider_missing_or_invalid_{provider.ValueKind}");
        if (provider.GetString() is not ("google.com" or "password"))
            throw new IdentityRejectedException("provider_not_allowed");
        if (!claims.TryGetProperty("auth_time", out var authTime) || authTime.ValueKind != JsonValueKind.Number || !authTime.TryGetInt64(out var seconds))
            throw new IdentityRejectedException($"auth_time_missing_or_invalid_{authTime.ValueKind}");
        return ValidateAccepted(uid, email.GetString()!, seconds, now);
    }

    private static FirebaseIdentity ValidateAccepted(string uid, string email, long authTime, DateTimeOffset now)
    {
        var age = now.ToUnixTimeSeconds() - authTime;
        if (age < -30 || age > 300) throw new IdentityRejectedException("authentication_not_recent");
        return new(uid, email.Trim().ToLowerInvariant());
    }

    private static bool ReadBoolean(object value) => value switch
    {
        bool boolean => boolean,
        JValue token when token.Type == JTokenType.Boolean => token.Value<bool>(),
        _ => false,
    };

    private static string? ReadString(object value) => value switch
    {
        string text => text,
        JValue token when token.Type == JTokenType.String => token.Value<string>(),
        _ => null,
    };

    private static string? ReadProvider(object value) => value switch
    {
        JObject token => token.Value<string>("sign_in_provider"),
        IReadOnlyDictionary<string, object> dictionary when dictionary.TryGetValue("sign_in_provider", out var provider) => ReadString(provider),
        _ => null,
    };

    private static bool ReadInt64(object value, out long result)
    {
        if (value is JValue token && token.Type == JTokenType.Integer)
        {
            result = token.Value<long>();
            return true;
        }
        try
        {
            result = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            result = 0;
            return false;
        }
    }
}
