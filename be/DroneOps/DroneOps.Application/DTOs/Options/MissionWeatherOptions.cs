namespace DroneOps.Application.DTOs.Options;

public sealed class MissionWeatherOptions
{
    public decimal? WindWarningMetersPerSecond { get; set; }

    public decimal? GustWarningMetersPerSecond { get; set; }

    public decimal? RainWarningMillimetersPerHour { get; set; }
}