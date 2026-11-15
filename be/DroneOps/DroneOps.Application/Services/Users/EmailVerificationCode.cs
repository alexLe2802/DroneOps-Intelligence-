using System.Globalization;
using System.Security.Cryptography;

namespace DroneOps.Application.Services.Users;

public static class EmailVerificationCode
{
    public static string Generate() => RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    public static bool IsValid(string code) => code.Length == 6 && code.All(character => character is >= '0' and <= '9');
}
