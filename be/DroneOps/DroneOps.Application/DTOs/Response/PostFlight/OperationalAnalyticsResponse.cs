using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneOps.Application.DTOs.Response.PostFlight;

public class OperationalAnalyticsResponse
{
    public int TotalFlights { get; set; }
    public int CompletedFlights { get; set; }
    public int AbortedOrFailedFlights { get; set; }
    public double TotalFlightHours { get; set; }
    public decimal TotalDistanceKilometers { get; set; }
    public int TotalIncidentsRecorded { get; set; }
    public double SuccessRatePercentage { get; set; }
}
