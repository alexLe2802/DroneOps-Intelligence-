using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

/// <summary>
/// Hộp thư cảnh báo bền vững theo từng người nhận. SSE có thể mất khi client ngắt kết nối,
/// còn bản ghi này tồn tại trong DB cho đến khi người dùng đánh dấu đã đọc.
/// </summary>
[Table("IncidentAlertDelivery")]
public sealed class IncidentAlertDelivery
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public Guid TransitionId { get; set; }
    public Guid RecipientUserId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }

    public Incident Incident { get; set; } = null!;
    public IncidentTransition Transition { get; set; } = null!;
    public User Recipient { get; set; } = null!;
}
