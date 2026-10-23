using System.Security.Claims;
using DroneOps.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers;

[ApiController]
[Authorize]
[Route("api/users/avatar")]
public sealed class AvatarController : ControllerBase
{
    private readonly IAvatarService _avatarService;

    public AvatarController(
        IAvatarService avatarService)
    {
        _avatarService = avatarService;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        CancellationToken cancellationToken)
    {
        string? userIdValue =
            User.FindFirstValue(
                ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(
                userIdValue,
                out Guid userId))
        {
            return Unauthorized();
        }

        string? signedUrl =
            await _avatarService.GetAvatarUrlAsync(
                userId,
                cancellationToken);

        Response.Headers.CacheControl = "no-store";

        return signedUrl is null
            ? NotFound()
            : Redirect(signedUrl);
    }

    [HttpPost]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        string? userIdValue =
            User.FindFirstValue(
                ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(
                userIdValue,
                out Guid userId))
        {
            return Unauthorized();
        }

        try
        {
            string? avatarUrl =
                await _avatarService.UploadAvatarAsync(
                    userId,
                    file,
                    cancellationToken);

            if (avatarUrl is null)
            {
                return NotFound();
            }

            return Ok(new
            {
                avatarUrl
            });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new
            {
                message = exception.Message
            });
        }
        catch (InvalidOperationException exception)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    message = exception.Message
                });
        }
        catch (HttpRequestException)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new
                {
                    message =
                        "Avatar upload failed. Please try again."
                });
        }
    }
}