using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneOps.Application.DTOs.Response.Incidents;

public class IncidentItemResponse
{
    public Guid Id { get; set; }
    public Guid MissionId { get; set; }
    public string MissionName { get; set; } = string.Empty;
    public Guid? UavId { get; set; }
    public string? UavCode { get; set; }
    public string? UavName { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

public class IncidentDetailResponse : IncidentItemResponse
{
    // Bằng chứng viễn trắc đóng băng tại thời điểm phát sinh sự cố (tọa độ, pin, tốc độ, độ cao...)
    public object? Evidence { get; set; }

    // Thông tin người nhận và xử lý sự cố (Audit log)
    public Guid? AcknowledgedBy { get; set; }
    public string? AcknowledgedByName { get; set; }
    public Guid? ResolvedBy { get; set; }
    public string? ResolvedByName { get; set; }
    public string? ResolutionNotes { get; set; }
}

public class PagedIncidentsResponse
{
    public List<IncidentItemResponse> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}
