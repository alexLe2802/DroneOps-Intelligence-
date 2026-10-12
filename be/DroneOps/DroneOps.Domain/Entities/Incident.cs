using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

// Chỉ định ánh xạ chính xác vào bảng "Incident" (số ít) trong CSDL
[Table("Incident")]
public class Incident
{
    public Guid Id { get; set; }

    public Guid MissionId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Severity { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Mission Mission { get; set; } = null!;
}