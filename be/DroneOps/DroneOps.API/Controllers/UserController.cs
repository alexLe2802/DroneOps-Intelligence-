using System.Security.Claims;
using DroneOps.Persistence.Auth;
using Npgsql;
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
    private readonly NpgsqlDataSource _dataSource;

    public UserController(
        IUserService userService, NpgsqlDataSource dataSource)
    {
        _userService = userService;
        _dataSource = dataSource;
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

        if (HttpContext.Items[typeof(SessionIdentity)] is SessionIdentity session)
        {
            var profile = await ReadSessionProfileAsync(session.Account.Id, cancellationToken);
            return profile is null ? NotFound(new { message = "User profile not found." }) : Ok(profile);
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

        if (HttpContext.Items[typeof(SessionIdentity)] is SessionIdentity session)
        {
            var fullName = request.FullName.Trim();
            if (string.IsNullOrWhiteSpace(fullName))
                return BadRequest(new { message = "Full name cannot be empty." });
            await using var command = _dataSource.CreateCommand("""
                UPDATE droneops.accounts SET display_name = @name, updated_at = now()
                WHERE id = @id AND is_active = true
                RETURNING id, display_name, email, role_code, created_at
                """);
            command.Parameters.AddWithValue("id", session.Account.Id);
            command.Parameters.AddWithValue("name", fullName);
            var profile = await ReadProfileAsync(command, cancellationToken);
            return profile is null ? NotFound(new { message = "User profile not found." }) : Ok(profile);
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

    private async Task<UserProfileResponse?> ReadSessionProfileAsync(Guid accountId, CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand("""
            SELECT id, display_name, email, role_code, created_at
            FROM droneops.accounts WHERE id = @id AND is_active = true
            """);
        command.Parameters.AddWithValue("id", accountId);
        return await ReadProfileAsync(command, ct);
    }

    private static async Task<UserProfileResponse?> ReadProfileAsync(NpgsqlCommand command, CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new UserProfileResponse
        {
            Id = reader.GetGuid(0), FullName = reader.GetString(1),
            Email = reader.GetString(2), Role = reader.GetString(3),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(4)
        };
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
