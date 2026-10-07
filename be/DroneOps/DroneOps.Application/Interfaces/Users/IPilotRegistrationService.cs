using DroneOps.Application.DTOs.Request.Users;
using DroneOps.Application.DTOs.Response.Auth;
using DroneOps.Application.DTOs.Response.Pages;
using DroneOps.Application.DTOs.Response.Users;

namespace DroneOps.Application.Interfaces.Users;

public interface IPilotRegistrationService
{
    // 1. Nộp đơn đăng ký
    Task RegisterAsync(CreatePilotRegistrationRequest request);

    // 2. Lấy danh sách đơn
    Task<PagedPilotRegistrationResponse> GetAllAsync(GetPilotRegistrationsRequest request);

    // 3. Xem chi tiết đơn
    Task<PilotRegistrationDetailResponse?> GetDetailAsync(Guid id);

    // 4. Admin phê duyệt đơn (tạo User với mật khẩu gốc + sinh mã OTP xác thực lần đầu gửi qua mail)
    Task<bool> ApprovePilotRegistrationAsync(Guid registrationId, ApprovePilotRegistrationRequest? request = null);

    // 5. Admin từ chối đơn kèm lý do
    Task<bool> RejectPilotRegistrationAsync(Guid registrationId, RejectPilotRegistrationRequest request);

    // 6. Pilot đăng nhập lần đầu: Nhập Email + Mật khẩu đã tạo + Mã OTP
    Task<LoginResponse> FirstTimeLoginAsync(PilotFirstTimeLoginRequest request, CancellationToken cancellationToken = default);
}