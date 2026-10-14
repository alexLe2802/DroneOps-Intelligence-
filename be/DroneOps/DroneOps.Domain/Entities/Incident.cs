using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

// Chỉ định ánh xạ chính xác vào bảng "Incident" trong CSDL
[Table("Incident")]
public class Incident
{
    public Guid Id { get; set; }
    public Guid MissionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Severity { get; set; } // Low, Medium, High, Critical
    public string Type { get; set; } = "Warning"; // CommunicationLoss, LowBattery, GeofenceBreach, WeatherWarning...
    public string Status { get; set; } = "Open"; // Open, Acknowledged, Resolved

    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    // Chứa JSON snapshot viễn trắc đóng băng tại thời điểm xảy ra sự cố (pin, tốc độ, độ cao...)
    [Column(TypeName = "jsonb")]
    public string? Evidence { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // Audit logs xử lý sự cố
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public Guid? AcknowledgedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedBy { get; set; }
    public string? ResolutionNotes { get; set; }

    // Navigation properties
    public Mission Mission { get; set; } = null!;

    [ForeignKey(nameof(AcknowledgedBy))]
    public User? Acknowledger { get; set; }

    [ForeignKey(nameof(ResolvedBy))]
    public User? Resolver { get; set; }
}