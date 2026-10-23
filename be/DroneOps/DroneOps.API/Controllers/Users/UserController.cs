using DroneOps.Application.DTOs.Request.Users;
using DroneOps.Application.DTOs.Response.Common;
using DroneOps.Application.DTOs.Response.Users;
using DroneOps.Application.Interfaces.Users;
using DroneOps.Persistence.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Claims;

namespace DroneOps.API.Controllers.Users;

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

    [HttpGet]
    [Authorize(Roles = "operation_manager")]
    public async Task<IActionResult> GetAllUsers(
     [FromQuery] UserFilterRequest request,
     CancellationToken cancellationToken)
    {
        PagedResponse<UserProfileResponse> result =
            await _userService.GetAllUsersAsync(
                request,
                cancellationToken);

        return Ok(result);
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
                WITH updated AS (
                    UPDATE public."User" SET "FullName" = @name, "UpdatedAt" = now()
                    WHERE "Id" = @id AND "IsActive" AND "WebAccessProvisioned"
                    RETURNING "Id", "FullName", "Email", "RoleId", "CreatedAt"
                )
                SELECT u."Id", u."FullName", u."Email",
                       CASE r."Name" WHEN 'Pilot' THEN 'uav_operator' WHEN 'uav_operator' THEN 'uav_operator' WHEN 'OperationsManager' THEN 'operation_manager' WHEN 'Admin' THEN 'operation_manager' WHEN 'SystemAdmin' THEN 'operation_manager' WHEN 'operations_manager' THEN 'operation_manager' WHEN 'operation_manager' THEN 'operation_manager' END,
                       u."CreatedAt"
                FROM updated u JOIN public."Role" r ON r."Id" = u."RoleId"
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
            SELECT u."Id", u."FullName", u."Email",
                   CASE r."Name" WHEN 'Pilot' THEN 'uav_operator' WHEN 'uav_operator' THEN 'uav_operator' WHEN 'OperationsManager' THEN 'operation_manager' WHEN 'Admin' THEN 'operation_manager' WHEN 'SystemAdmin' THEN 'operation_manager' WHEN 'operations_manager' THEN 'operation_manager' WHEN 'operation_manager' THEN 'operation_manager' END,
                   u."CreatedAt"
            FROM public."User" u JOIN public."Role" r ON r."Id" = u."RoleId"
            WHERE u."Id" = @id AND u."IsActive" AND u."WebAccessProvisioned"
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
            Id = reader.GetGuid(0),
            FullName = reader.GetString(1),
            Email = reader.GetString(2),
            Role = reader.GetString(3),
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

    [Authorize(Roles = "uav_operator")]
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

    [Authorize(Roles = "uav_operator")]
    [HttpPut("pilot-profile")]
    [ProducesResponseType(
    typeof(UpdatePilotProfileResponse),
    StatusCodes.Status200OK)]
    [ProducesResponseType(
    StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
    StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
    StatusCodes.Status404NotFound)]
    [ProducesResponseType(
    StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdatePilotProfile(
    [FromBody] UpdatePilotProfileRequest request,
    CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out Guid userId))
        {
            return Unauthorized(new
            {
                message = "Invalid access token."
            });
        }

        if (request.PilotLicenseNo is not null || request.LicenseIssuedDate.HasValue
            || request.LicenseExpiredDate.HasValue || request.LicenseImageUrls is not null)
        {
            return BadRequest(new { message = "Submit license changes with documents to POST /api/users/pilot-update-requests. This endpoint updates personal profile fields only." });
        }

        try
        {
            UpdatePilotProfileResponse? result =
                await _userService.UpdatePilotProfileAsync(
                    userId,
                    request,
                    cancellationToken);

            if (result is null)
            {
                return NotFound(new
                {
                    message = "Pilot profile not found."
                });
            }

            return Ok(result);
        }
        catch (InvalidOperationException ex)
            when (ex.Message.Contains(
                "pending",
                StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new
            {
                message = ex.Message
            });
        }
    }
    

   
    // =========================================================================
    // CÁC ENDPOINT MỚI: ĐỔI EMAIL & ĐỔI MẬT KHẨU CÓ XÁC THỰC
    // =========================================================================

    /// <summary>
    /// 1. Yêu cầu đổi email: Kiểm tra mật khẩu hiện tại và gửi mã OTP về email mới
    /// </summary>
    [HttpPost("change-email/request")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RequestChangeEmail(
        [FromBody] ChangeEmailRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out Guid userId))
        {
            return Unauthorized(new { message = "Invalid access token." });
        }

        if (HttpContext.Items[typeof(SessionIdentity)] is SessionIdentity session)
        {
            userId = session.Account.Id;
        }

        try
        {
            var message = await _userService.RequestChangeEmailAsync(userId, request, cancellationToken);
            return Ok(new { message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 2. Xác thực đổi email: Kiểm tra mã OTP và cập nhật email mới vào Database
    /// </summary>
    [HttpPost("change-email/confirm")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ConfirmChangeEmail(
        [FromBody] VerifyChangeEmailRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out Guid userId))
        {
            return Unauthorized(new { message = "Invalid access token." });
        }

        if (HttpContext.Items[typeof(SessionIdentity)] is SessionIdentity session)
        {
            userId = session.Account.Id;
        }

        try
        {
            var success = await _userService.ConfirmChangeEmailAsync(userId, request, cancellationToken);
            return Ok(new { success, message = "Email has been successfully changed." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 3. Đổi mật khẩu: Xác thực mật khẩu cũ và cập nhật mật khẩu mới chuẩn bảo mật
    /// </summary>
    [HttpPost("change-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out Guid userId))
        {
            return Unauthorized(new { message = "Invalid access token." });
        }

        if (HttpContext.Items[typeof(SessionIdentity)] is SessionIdentity session)
        {
            userId = session.Account.Id;
        }

        try
        {
            var success = await _userService.ChangePasswordAsync(userId, request, cancellationToken);
            return Ok(new { success, message = "Password has been successfully changed." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}