using DroneOps.Application.DTOs.Request.Auth;
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

    /// <summary>
    /// Quên mật khẩu: Gửi mã OTP xác nhận về email (Áp dụng chung cho cả Admin và Pilot)
    /// </summary>
    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _authService.ForgotPasswordAsync(request, cancellationToken);
            return Ok(new
            {
                Success = result,
                Message = "Verification code has been sent to your email."
            });
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
            return BadRequest(new { Message = detail });
        }
    }

    /// <summary>
    /// Đặt lại mật khẩu mới bằng mã OTP
    /// </summary>
    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _authService.ResetPasswordAsync(request, cancellationToken);
            return Ok(new
            {
                Success = result,
                Message = "Password has been reset successfully. You can now login with your new password."
            });
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
            return BadRequest(new { Message = detail });
        }
    }
}