using DroneOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Application.Services.Users;

public static class ProfileConcurrency
{
    public static void Require(User user, long? expectedVersion)
    {
        if (expectedVersion is null or < 1)
            throw new ArgumentException("A profile version is required. Reload your profile before saving.");
        if (expectedVersion != user.ProfileVersion)
            throw new DbUpdateConcurrencyException("Your profile changed since it was loaded. Reload before saving.");
    }
}
