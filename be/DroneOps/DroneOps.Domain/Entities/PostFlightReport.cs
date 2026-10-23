using System.ComponentModel.DataAnnotations.Schema;

namespace DroneOps.Domain.Entities;

// Chỉ định ánh xạ chính xác vào bảng "PostFlightReport" trong CSDL
[Table("PostFlightReport")]
public class PostFlightReport
{
    public Guid Id { get; set; }

    public Guid MissionId { get; set; }

    public int Revision { get; set; } = 1;
    public bool IsCurrent { get; set; } = true;
    public string GenerationStatus { get; set; } = "Queued";
    public DateTimeOffset? CoveredTelemetryThrough { get; set; }
    public DateTimeOffset? GenerationStartedAt { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public string? FailureReason { get; set; }
    public Guid? RequestedBy { get; set; }
    public string? GenerationReason { get; set; }

    // Thống kê thời gian bay (phút)
    public double TotalFlightDurationMinutes { get; set; }

    // Thống kê độ cao (mét)
    public decimal? MaxAltitude { get; set; }
    public decimal? AvgAltitude { get; set; }

    // Thống kê tốc độ (m/s)
    public decimal? MaxSpeed { get; set; }
    public decimal? AvgSpeed { get; set; }

    // Thống kê mức pin (%)
    public int? InitialBatteryLevel { get; set; }
    public int? FinalBatteryLevel { get; set; }
    public int? BatteryConsumedPercentage { get; set; }

    // Ước tính tổng quãng đường bay (mét)
    public decimal? TotalDistanceMeters { get; set; }

    // Số lượng gói tin viễn trắc và sự cố ghi nhận
    public int TotalTelemetryRecords { get; set; }
    public int TotalIncidents { get; set; }

    // Đánh giá trạng thái & chất lượng chuyến bay
    public string Status { get; set; } = "Completed"; // Flight outcome, separate from GenerationStatus.
    public string? Summary { get; set; }
    public string? SafetyEvaluation { get; set; }
    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // Navigation property liên kết với Mission
    public Mission Mission { get; set; } = null!;
}
