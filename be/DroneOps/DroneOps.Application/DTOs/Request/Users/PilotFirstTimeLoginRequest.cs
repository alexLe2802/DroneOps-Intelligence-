using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneOps.Application.DTOs.Request.Users;

/// <summary>
/// DTO phục vụ đăng nhập lần đầu cho Pilot:
/// Nhập Email + Mật khẩu đã đăng ký ban đầu + Mã OTP gửi qua email
/// </summary>
public class PilotFirstTimeLoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Otp { get; set; } = string.Empty;
}
