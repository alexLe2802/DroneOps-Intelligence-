using DroneOps.Application.DTOs.Request.Users;
using DroneOps.Application.DTOs.Response.Common;
using DroneOps.Application.DTOs.Response.Users;

namespace DroneOps.Application.Interfaces.Users;

public interface IUserService
{
    Task<UserProfileResponse?> GetProfileAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<UserProfileResponse?> UpdateProfileAsync(
        Guid userId,
        UpdateProfileRequest request,
        CancellationToken cancellationToken = default);

    Task<PilotProfileResponse?> GetPilotProfileAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<PagedResponse<UserProfileResponse>> GetAllUsersAsync(
        UserFilterRequest request,
        CancellationToken cancellationToken = default);

    Task<UpdatePilotProfileResponse?> UpdatePilotProfileAsync(
        Guid userId,
        UpdatePilotProfileRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PilotUpdateRequestResponse>>GetPendingPilotUpdateRequestsAsync(
        CancellationToken cancellationToken = default);

    Task<PilotUpdateRequestResponse?>GetPilotUpdateRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken = default);

    Task<PilotUpdateRequestResponse?> ApprovePilotUpdateRequestAsync(
            Guid requestId,
            Guid reviewerId,
            CancellationToken cancellationToken = default);

    Task<PilotUpdateRequestResponse?>RejectPilotUpdateRequestAsync(
            Guid requestId,
            Guid reviewerId,
            RejectPilotUpdateRequest request,
            CancellationToken cancellationToken = default);

    // 1. Yêu cầu đổi email (Gửi OTP sang email mới)
    Task<string> RequestChangeEmailAsync(
        Guid userId,
        ChangeEmailRequest request,
        CancellationToken cancellationToken = default);

    // 2. Xác thực OTP và hoàn tất đổi email
    Task<bool> ConfirmChangeEmailAsync(
        Guid userId,
        VerifyChangeEmailRequest request,
        CancellationToken cancellationToken = default);

    // 3. Đổi mật khẩu tài khoản đang đăng nhập
    Task<bool> ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default);
}