using System.Security.Claims;
using DroneOps.Application.DTOs.Request.Users;
using DroneOps.Application.DTOs.Response.Users;
using DroneOps.Application.Interfaces.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers.Users;

[ApiController]
[Authorize]
[Route("api/users")]
public sealed class UserController : ControllerBase
{
    private readonly IUserService _userService;

    public UserController(
        IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet("profile")]
    [ProducesResponseType(
        typeof(UserProfileResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfile(
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out Guid userId))
        {
            return Unauthorized(new
            {
                message = "Invalid access token."
            });
        }

        UserProfileResponse? result =
            await _userService.GetProfileAsync(
                userId,
                cancellationToken);

        if (result is null)
        {
            return NotFound(new
            {
                message = "User profile not found."
            });
        }

        return Ok(result);
    }

    [HttpPut("profile")]
    [ProducesResponseType(
        typeof(UserProfileResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out Guid userId))
        {
            return Unauthorized(new
            {
                message = "Invalid access token."
            });
        }

        UserProfileResponse? result =
            await _userService.UpdateProfileAsync(
                userId,
                request,
                cancellationToken);

        if (result is null)
        {
            return NotFound(new
            {
                message = "User profile not found."
            });
        }

        return Ok(result);
    }

    private bool TryGetCurrentUserId(
        out Guid userId)
    {
        string? userIdClaim =
            User.FindFirstValue(
                ClaimTypes.NameIdentifier);

        return Guid.TryParse(
            userIdClaim,
            out userId);
    }

    [Authorize(Roles = "Pilot")]
    [HttpGet("pilot-profile")]
    public async Task<IActionResult> GetPilotProfile(
      CancellationToken cancellationToken)
    {
        var userIdClaim =
            User.FindFirst(ClaimTypes.NameIdentifier);

        if (userIdClaim is null)
        {
            return Unauthorized();
        }

        var profile =
            await _userService.GetPilotProfileAsync(
                Guid.Parse(userIdClaim.Value),
                cancellationToken);

        if (profile is null)
        {
            return NotFound(new
            {
                Message = "Pilot profile not found."
            });
        }

        return Ok(profile);
    }
}
