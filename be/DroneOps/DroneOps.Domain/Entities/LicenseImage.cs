using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

[Table("LicenseImage")]
public class LicenseImage
{
    public Guid Id { get; set; }

    public string ImageUrl { get; set; } = string.Empty;

    public string? ImageType { get; set; }

    public DateTime CreatedAt { get; set; }

    // Dùng khi đăng ký Pilot ban đầu
    public Guid? PilotRegistrationId { get; set; }

    // Dùng khi yêu cầu update license
    public Guid? PilotUpdateRequestId { get; set; }

    // Chỉ có khi ảnh đã được admin duyệt
    public Guid? PilotId { get; set; }

    public PilotRegistration? PilotRegistration { get; set; }

    public PilotUpdateRequest? PilotUpdateRequest { get; set; }

    public Pilot? Pilot { get; set; }
}