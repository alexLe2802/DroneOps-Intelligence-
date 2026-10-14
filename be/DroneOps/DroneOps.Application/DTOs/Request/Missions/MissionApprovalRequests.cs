using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneOps.Application.DTOs.Request.Missions;

public class GetPendingApprovalsRequest
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Search { get; set; }
    public Guid? UavId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public class ApproveMissionRequest
{
    public string? Comment { get; set; }
    public string? ExpectedStatus { get; set; } = "Pending Review";
    public Guid? ValidationRunId { get; set; }
}

public class RejectMissionRequest
{
    public string Reason { get; set; } = string.Empty;
    public string? ExpectedStatus { get; set; } = "Pending Review";
}
