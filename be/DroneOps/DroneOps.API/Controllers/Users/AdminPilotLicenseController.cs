using System.Security.Claims;
using DroneOps.Application.DTOs.Request.License;
using DroneOps.Application.DTOs.Response.License;
using DroneOps.Application.Services.License;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers.Users;

[ApiController]
[Route("api/admin/licenses")]
[Authorize(Roles = "operation_manager")]
public sealed class AdminPilotLicenseController : ControllerBase
{
    private readonly IPilotLicenseWorkflow _licenseWorkflow;

    public AdminPilotLicenseController(IPilotLicenseWorkflow licenseWorkflow)
    {
        _licenseWorkflow = licenseWorkflow;
    }

    /// <summary>
    /// Lấy trạng thái hiện tại của giấy phép bay (qua PilotId hoặc UserId)
    /// </summary>
    [HttpGet("{id:guid}/status")]
    [ProducesResponseType(typeof(PilotLicenseStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(Guid id, CancellationToken cancellationToken)
    {
        var result = await _licenseWorkflow.GetStatusAsync(id, cancellationToken);
        if (result is null)
        {
            return NotFound(new { message = "Pilot not found." });
        }
        return Ok(result);
    }

    /// <summary>
    /// Lấy lịch sử thay đổi trạng thái giấy phép bay
    /// </summary>
    [HttpGet("{id:guid}/history")]
    [ProducesResponseType(typeof(IReadOnlyList<PilotLicenseAuditResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHistory(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _licenseWorkflow.GetHistoryAsync(id, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Kích hoạt lại (Activate) giấy phép bay
    /// </summary>
    [HttpPut("{id:guid}/activate")]
    [ProducesResponseType(typeof(PilotLicenseStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Activate(
        Guid id,
        [FromBody] PilotLicenseDecisionRequest request,
        CancellationToken cancellationToken) =>
        Decide(id, "activate", request, cancellationToken);

    /// <summary>
    /// Vô hiệu hóa (Deactivate / Inactive) giấy phép bay của phi công
    /// </summary>
    [HttpPut("{id:guid}/deactivate")]
    [ProducesResponseType(typeof(PilotLicenseStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Deactivate(
        Guid id,
        [FromBody] PilotLicenseDecisionRequest request,
        CancellationToken cancellationToken) =>
        Decide(id, "deactivate", request, cancellationToken);

    private async Task<IActionResult> Decide(
        Guid id,
        string operation,
        PilotLicenseDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var actorValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(actorValue, out var actor))
        {
            return Unauthorized();
        }

        try
        {
            var result = await _licenseWorkflow.DecideAsync(id, actor, operation, request, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
