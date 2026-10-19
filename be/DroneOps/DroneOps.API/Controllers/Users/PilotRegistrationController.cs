using System.Text.Json;
using FluentValidation;
using DroneOps.API.Storage;
using Microsoft.AspNetCore.RateLimiting;
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
    private readonly UserMediaStorage _storage;

    public PilotRegistrationController(IPilotRegistrationService service, UserMediaStorage storage)
    {
        _service = service;
        _storage = storage;
    }

    [AllowAnonymous, HttpPost("with-files"), RequestSizeLimit(106 * 1024 * 1024)]
    public async Task<IActionResult> RegisterWithFiles([FromForm] string data, [FromForm] List<IFormFile> files, CancellationToken ct)
    {
        CreatePilotRegistrationRequest? request;
        try { request = JsonSerializer.Deserialize<CreatePilotRegistrationRequest>(data, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (JsonException) { return BadRequest(new { message = "Invalid registration data." }); }
        if (request is null || !TryValidateModel(request)) return ValidationProblem(ModelState);
        var validation = await HttpContext.RequestServices.GetRequiredService<IValidator<CreatePilotRegistrationRequest>>().ValidateAsync(request, ct);
        if (!validation.IsValid) return BadRequest(new { message = validation.Errors[0].ErrorMessage });
        if (files.Count is < 1 or > 10) return BadRequest(new { message = "Attach between 1 and 10 license files." });
        var uploaded = new List<string>();
        try
        {
            foreach (var file in files) await UserMediaStorage.ValidateAsync(file, false, ct);
            var folder = Guid.NewGuid().ToString("N");
            foreach (var file in files)
            {
                var extension = file.ContentType == "application/pdf" ? ".pdf" : file.ContentType == "image/png" ? ".png" : file.ContentType == "image/webp" ? ".webp" : ".jpg";
                var path = $"licenses/{folder}/{Guid.NewGuid():N}{extension}";
                await _storage.UploadAsync(path, file, ct);
                uploaded.Add(path);
            }
            request.LicenseImageUrls = uploaded;
            await _service.RegisterAsync(request);
            return Ok(new { message = "Registration submitted successfully." });
        }
        catch (Exception e)
        {
            try { await _storage.DeleteAsync(uploaded, CancellationToken.None); }
            catch (Exception cleanup) { HttpContext.RequestServices.GetRequiredService<ILogger<PilotRegistrationController>>().LogWarning(cleanup, "License upload cleanup failed."); }
            var message = e is ArgumentException || e is InvalidOperationException ? e.Message : "Unable to submit registration. Check your details or try again.";
            return BadRequest(new { message });
        }
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
            return BadRequest(new
            {
                message = ex.InnerException?.Message ?? ex.Message,
                detail = ex.InnerException?.ToString()
            });
        }
    }

    /// <summary>
   
    /// </summary>
    [Authorize(Roles = "operation_manager")]
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] GetPilotRegistrationsRequest request)
    {
        var result = await _service.GetAllAsync(request);
        return Ok(result);
    }

    /// <summary>
    
    /// </summary>
    [Authorize(Roles = "operation_manager")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetDetail(Guid id)
    {
        var result = await _service.GetDetailAsync(id);
        if (result == null)
        {
            return NotFound(new { Message = "Pilot registration not found." });
        }
        result.LicenseImageUrls = (await Task.WhenAll(result.LicenseImageUrls
            .Where(path => path.StartsWith("licenses/"))
            .Select(path => _storage.SignAsync(path, HttpContext.RequestAborted)))).ToList();
        return Ok(result);
    }

    /// <summary>
    /// Duyệt đơn Pilot: Dùng mật khẩu gốc tạo User + gửi mã OTP xác thực đăng nhập lần đầu qua Email (Operation Manager)
    /// </summary>
    [Authorize(Roles = "operation_manager")]
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApprovePilotRegistrationRequest? request)
    {
        try
        {
            var result = await _service.ApprovePilotRegistrationAsync(id, request);
            return Ok(new
            {
                Success = result,
                Message = "Registration approved. OTP has been sent to the pilot's email."
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Từ chối đơn Pilot kèm lý do -> Gửi email thông báo (Operation Manager)
    /// </summary>
    [Authorize(Roles = "operation_manager")]
    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectPilotRegistrationRequest request)
    {
        try
        {
            var result = await _service.RejectPilotRegistrationAsync(id, request);
            return Ok(new
            {
                Success = result,
                Message = "Registration rejected and notification email has been sent."
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
    [HttpPost("first-time-login/resend-otp")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ResendFirstLoginOtp([FromBody] DroneOps.Application.DTOs.Request.Auth.LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "Email and password are required." });
        try
        {
            await _service.ResendFirstLoginOtpAsync(request.Email, request.Password, cancellationToken);
            return Ok(new { message = "A new OTP has been emailed. It expires in 10 minutes.", expiresInSeconds = 600, retryAfterSeconds = 60 });
        }
        catch (Exception e) { return BadRequest(new { message = e.Message }); }
    }

    [AllowAnonymous]
    [HttpPost("first-time-login")]
    [EnableRateLimiting("login")]
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