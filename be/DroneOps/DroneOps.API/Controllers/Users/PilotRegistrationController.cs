using DroneOps.Application.DTOs.Request.Users;
using DroneOps.Application.Interfaces.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers;

[ApiController]
[Route("api/pilot-registrations")]
public class PilotRegistrationController : ControllerBase
{
    private readonly IPilotRegistrationService _service;

    public PilotRegistrationController(IPilotRegistrationService service)
    {
        _service = service;
    }

    /// <summary>
    /// Nộp đơn đăng ký Pilot (kèm mật khẩu tự đặt)
    /// </summary>
    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Register(CreatePilotRegistrationRequest request)
    {
        try
        {
            await _service.RegisterAsync(request);
            return Ok(new
            {
                Message = "Registration submitted successfully."
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Lấy danh sách đơn đăng ký (Admin)
    /// </summary>
    [Authorize(Roles = "Admin,SystemAdmin")]
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] GetPilotRegistrationsRequest request)
    {
        var result = await _service.GetAllAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// Lấy chi tiết đơn đăng ký theo ID (Admin)
    /// </summary>
    [Authorize(Roles = "Admin,SystemAdmin")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetDetail(Guid id)
    {
        var result = await _service.GetDetailAsync(id);
        if (result == null)
        {
            return NotFound(new { Message = "Pilot registration not found." });
        }
        return Ok(result);
    }

    /// <summary>
    /// Duyệt đơn Pilot: Dùng mật khẩu gốc tạo User + gửi mã OTP xác thực đăng nhập lần đầu qua Email (Admin)
    /// </summary>
    [Authorize(Roles = "Admin,SystemAdmin")]
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApprovePilotRegistrationRequest? request)
    {
        try
        {
            var result = await _service.ApprovePilotRegistrationAsync(id, request);
            return Ok(new
            {
                Success = result,
                Message = "Đơn đăng ký đã được duyệt. Mã OTP xác thực lần đầu đã được gửi về email của Pilot."
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Từ chối đơn Pilot kèm lý do -> Gửi email thông báo (Admin)
    /// </summary>
    [Authorize(Roles = "Admin,SystemAdmin")]
    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectPilotRegistrationRequest request)
    {
        try
        {
            var result = await _service.RejectPilotRegistrationAsync(id, request);
            return Ok(new
            {
                Success = result,
                Message = "Đơn đăng ký đã bị từ chối và email thông báo đã được gửi."
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Đăng nhập lần đầu cho Pilot: Nhập Email + Mật khẩu đã đăng ký + Mã OTP gửi về email
    /// </summary>
    [AllowAnonymous]
    [HttpPost("first-time-login")]
    public async Task<IActionResult> FirstTimeLogin([FromBody] PilotFirstTimeLoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.FirstTimeLoginAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }
}