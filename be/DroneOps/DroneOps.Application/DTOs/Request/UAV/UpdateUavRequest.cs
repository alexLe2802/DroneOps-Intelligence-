using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneOps.Application.DTOs.Request.UAVs;

public class UpdateUavRequest
{
    public string UavCode { get; set; } = string.Empty;
    public string UavName { get; set; } = string.Empty;
    public string? Description { get; set; }
}