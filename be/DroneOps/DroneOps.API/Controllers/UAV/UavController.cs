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
    /// Pilot xóa UAV của chính mình ở mọi trạng thái; manager có thể xóa mọi UAV.
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

        var result = await _uavService.DeleteAsync(
            userId,
            id,
            canManageAll,
            cancellationToken);

        if (!result)
        {
            return NotFound(new { message = "UAV not found or does not belong to the authenticated pilot." });
        }

        return Ok(new { message = "UAV deleted successfully." });
    }
}
