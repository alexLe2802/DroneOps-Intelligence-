using System.Security.Claims;
using DroneOps.Application.DTOs.Users;
using DroneOps.Application.Interfaces.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers;

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
}
