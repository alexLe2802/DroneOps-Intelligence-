using System.Security.Claims;
using DroneOps.API.Storage;
using DroneOps.Persistence.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.API.Controllers;

[ApiController, Authorize, Route("api/users/avatar")]
public sealed class AvatarController(AppDbContext db, UserMediaStorage storage, ILogger<AvatarController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Unauthorized();
        var path = await db.Users.Where(u => u.Id == id).Select(u => u.AvatarPath).FirstOrDefaultAsync(ct);
        Response.Headers.CacheControl = "no-store";
        return path is null ? NotFound() : Redirect(await storage.SignAsync(path, ct));
    }

    [HttpPost, RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Unauthorized();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();
        try { await UserMediaStorage.ValidateAsync(file, true, ct); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        var path = $"avatars/{id}/{Guid.NewGuid():N}";
        try { await storage.UploadAsync(path, file, ct); }
        catch (InvalidOperationException e) { return StatusCode(503, new { message = e.Message }); }
        catch (HttpRequestException) { return StatusCode(502, new { message = "Avatar upload failed. Please try again." }); }
        var old = user.AvatarPath;
        user.AvatarPath = path;
        try { await db.SaveChangesAsync(ct); }
        catch { await Cleanup(path); throw; }
        if (old is not null) await Cleanup(old);
        return Ok(new { avatarUrl = "/api/users/avatar?v=" + Uri.EscapeDataString(path) });
    }

    private async Task Cleanup(string path)
    {
        try { await storage.DeleteAsync([path], CancellationToken.None); }
        catch (Exception e) { logger.LogWarning(e, "Unable to remove replaced avatar."); }
    }
}
