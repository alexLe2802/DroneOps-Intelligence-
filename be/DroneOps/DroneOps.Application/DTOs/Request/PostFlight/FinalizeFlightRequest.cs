using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneOps.Application.DTOs.Request.PostFlight;

public class FinalizeFlightRequest
{
    public string? Notes { get; set; }
    public string? Summary { get; set; }
}