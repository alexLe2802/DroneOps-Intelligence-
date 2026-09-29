using DroneOps.Application.DTOs.Auth;
using DroneOps.Application.Interfaces.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DroneOps.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new
            {
                Message = "Email and password are required."
            });
        }

        var result = await _authService.LoginAsync(request, cancellationToken);

        if (result is null)
        {
            return Unauthorized(new
            {
                Message = "Invalid email or password."
            });
        }

        return Ok(result);
    }

    /// <summary>
    /// Đăng ký tài khoản ADMIN (gán role Admin)
    /// </summary>
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = await _authService.RegisterAsync(request, cancellationToken);
            return Ok(new
            {
                Message = message
            });
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
            return BadRequest(new
            {
                Message = detail
            });
        }
    }

    /// <summary>
    /// Đăng ký tài khoản PILOT / USER (gán role Pilot)
    /// </summary>
    [AllowAnonymous]
    [HttpPost("register-pilot")]
    public async Task<IActionResult> RegisterPilot(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = await _authService.RegisterPilotAsync(request, cancellationToken);
            return Ok(new
            {
                Message = message
            });
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
            return BadRequest(new
            {
                Message = detail
            });
        }
    }

    /// <summary>
    /// Xác thực mã OTP để kích hoạt tài khoản
    /// </summary>
    [AllowAnonymous]
    [HttpPost("verify-register")]
    public async Task<IActionResult> VerifyRegister(
        [FromBody] VerifyRegisterRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var success = await _authService.VerifyRegisterAsync(request, cancellationToken);
            return Ok(new
            {
                Success = success,
                Message = "Account verified successfully. You can now login."
            });
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
            return BadRequest(new
            {
                Message = detail
            });
        }
    }
}