using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

/// <summary>
/// Nhật ký bất biến của vòng đời sự cố. Mỗi thay đổi trạng thái tạo một dòng mới;
/// tuyệt đối không cập nhật hoặc xóa dòng lịch sử đã tồn tại.
/// </summary>
[Table("IncidentTransition")]
public sealed class IncidentTransition
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public Guid? ChangedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Incident Incident { get; set; } = null!;
    public User? Actor { get; set; }
    public ICollection<IncidentAlertDelivery> AlertDeliveries { get; set; } = [];
}
