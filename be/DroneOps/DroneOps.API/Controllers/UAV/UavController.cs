using System.Security.Claims;
using DroneOps.Application.DTOs.Request.UAVs;
using DroneOps.Application.Interfaces.UAVs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers.UAVs;

[ApiController]
[Route("api/uavs")]
[Authorize(Roles = "uav_operator,operation_manager,operations_manager,Admin")]
public class UavController : ControllerBase
{
    private readonly IUavService _uavService;

    public UavController(IUavService uavService)
    {
        _uavService = uavService;
    }

    /// <summary>
    /// Đăng ký / Tạo mới UAV (Trạng thái mặc định: Pending)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateUavRequest request,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(
            User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var result = await _uavService.CreateAsync(
            userId,
            request,
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Xem danh sách UAV của bản thân (Pilot)
    /// </summary>
    [HttpGet("myUAV")]
    public async Task<IActionResult> GetMyUavs(
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(
            User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var result = await _uavService.GetMyUavsAsync(
            userId,
            cancellationToken);

        return Ok(result);
    }

    /// <summary>Đăng ảnh lần đầu hoặc thay thế ảnh hiện tại của UAV.</summary>
    [HttpPost("{id:guid}/image")]
    [HttpPut("{id:guid}/image")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UploadOrReplaceImage(
        Guid id,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actorUserId, out var actorRole))
            return Unauthorized(new { message = "Invalid authenticated user." });
        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _uavService.UploadOrReplaceUavImageAsync(
                id, actorUserId, actorRole, stream, file.FileName,
                file.ContentType, file.Length, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException exception) { return NotFound(new { message = exception.Message }); }
        catch (UnauthorizedAccessException exception) { return StatusCode(403, new { message = exception.Message }); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (InvalidOperationException exception) { return StatusCode(503, new { message = exception.Message }); }
        catch (HttpRequestException) { return StatusCode(502, new { message = "UAV image storage is temporarily unavailable." }); }
    }

    /// <summary>Mở ảnh hiện tại của UAV qua signed URL ngắn hạn.</summary>
    [HttpGet("{id:guid}/image")]
    [AllowAnonymous]
    public async Task<IActionResult> GetImage(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var image = await _uavService.GetUavImageAsync(id, cancellationToken);
            if (image is null) return NotFound(new { message = "UAV image not found." });
            Response.Headers.CacheControl = "no-store";
            return Redirect(image.Value.Url);
        }
        catch (InvalidOperationException exception) { return StatusCode(503, new { message = exception.Message }); }
        catch (HttpRequestException) { return StatusCode(502, new { message = "UAV image storage is temporarily unavailable." }); }
    }

    /// <summary>Xóa ảnh hiện tại nhưng giữ nguyên UAV.</summary>
    [HttpDelete("{id:guid}/image")]
    public async Task<IActionResult> DeleteImage(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actorUserId, out var actorRole))
            return Unauthorized(new { message = "Invalid authenticated user." });
        try
        {
            var deleted = await _uavService.DeleteUavImageAsync(
                id, actorUserId, actorRole, cancellationToken);
            return deleted ? NoContent() : NotFound(new { message = "UAV not found." });
        }
        catch (UnauthorizedAccessException exception) { return StatusCode(403, new { message = exception.Message }); }
    }

    /// <summary>
    /// Cập nhật thông tin UAV
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateUavRequest request,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(
            User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var result = await _uavService.UpdateAsync(
            userId,
            id,
            request,
            cancellationToken);

        if (!result)
        {
            return NotFound(new { message = "UAV not found or unauthorized." });
        }

        return Ok(new { message = "UAV updated successfully." });
    }

    /// <summary>
    /// Pilot xóa UAV chưa bind của chính mình; manager có thể xóa UAV chưa bind.
    /// UAV đã gắn flight controller phải deactivate, không được xóa để lách binding.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "uav_operator,operation_manager,operations_manager,Admin")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Unauthorized(new { message = "Invalid authenticated user." });
        }

        var canManageAll = User.IsInRole("operation_manager") ||
            User.IsInRole("operations_manager") || User.IsInRole("Admin");

        bool result;
        try
        {
            result = await _uavService.DeleteAsync(
                userId,
                id,
                canManageAll,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }

        if (!result)
        {
            return NotFound(new { message = "UAV not found or does not belong to the authenticated pilot." });
        }

        return Ok(new { message = "UAV deleted successfully." });
    }

    private bool TryGetActor(out Guid userId, out string role)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        role = User.IsInRole("operation_manager") || User.IsInRole("operations_manager") || User.IsInRole("Admin")
            ? "operation_manager"
            : User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        return Guid.TryParse(value, out userId);
    }
}
