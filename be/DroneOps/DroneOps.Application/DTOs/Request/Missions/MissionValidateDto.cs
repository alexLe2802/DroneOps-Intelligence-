using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneOps.Application.DTOs.Request.Missions;

public class MissionValidateDto
{
    public Guid MissionId { get; set; }
    public List<WaypointDto>? Waypoints { get; set; }
}

public class WaypointDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Altitude { get; set; }
}
