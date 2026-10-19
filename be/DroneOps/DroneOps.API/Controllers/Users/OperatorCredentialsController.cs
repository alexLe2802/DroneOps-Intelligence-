using DroneOps.API.Storage;
using DroneOps.Persistence.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.API.Controllers;

[ApiController, Authorize(Roles = "operation_manager"), Route("api/users/{userId:guid}")]
public sealed class OperatorCredentialsController(AppDbContext db, UserMediaStorage storage) : ControllerBase
{
    [HttpGet("avatar")]
    public async Task<IActionResult> Avatar(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Include(x => x.Role).FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null || DroneOps.Domain.CanonicalRoles.Normalize(user.Role.Name) != "uav_operator") return NotFound();
        Response.Headers.CacheControl = "no-store";
        return user.AvatarPath is null ? NotFound() : Redirect(await storage.SignAsync(user.AvatarPath, ct));
    }

    [HttpGet("credentials")]
    public async Task<IActionResult> Credentials(Guid userId, CancellationToken ct)
    {
        var pilot = await db.Pilots.AsNoTracking().Include(x => x.User).ThenInclude(x => x.Role)
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (pilot is null) return NotFound(new { message = "This operator does not have a pilot profile yet." });
        if (DroneOps.Domain.CanonicalRoles.Normalize(pilot.User.Role.Name) != "uav_operator") return NotFound();
        var paths = await db.LicenseImages.AsNoTracking().Where(x => x.PilotId == pilot.Id).Select(x => x.ImageUrl).ToListAsync(ct);
        if (paths.Count == 0)
        {
            // Legacy registrations predate direct PilotId associations. Match both identity and the current license.
            paths = await db.LicenseImages.AsNoTracking().Where(x => x.PilotRegistration != null
                && x.PilotRegistration.Status == "Approved"
                && x.PilotRegistration.Email.ToLower() == pilot.User.Email.ToLower()
                && x.PilotRegistration.PilotLicenseNo == pilot.PilotLicenseNo)
                .Select(x => x.ImageUrl).ToListAsync(ct);
        }
        var urls = new List<string>();
        var documentsAvailable = true;
        foreach (var path in paths.Where(x => x.StartsWith("licenses/")))
        {
            try { urls.Add(await storage.SignAsync(path, ct)); }
            catch (Exception e) when (e is HttpRequestException or InvalidOperationException or ArgumentException) { documentsAvailable = false; }
        }
        return Ok(new { pilot.PhoneNumber, pilot.DateOfBirth, pilot.ExperienceYears,
            pilot.PilotLicenseNo, pilot.LicenseIssuedDate, pilot.LicenseExpiredDate,
            licenseImageUrls = urls, documentsAvailable });
    }
}
