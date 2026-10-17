using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneOps.Application.DTOs.Request.Missions;

public class CancelMissionRequest
{
    public string? Reason { get; set; }
}