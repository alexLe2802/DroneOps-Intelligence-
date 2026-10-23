namespace DroneOps.Application.Settings;

public sealed class PostFlightOptions
{
    public const string SectionName = "PostFlight";

    public int CompletionScanIntervalSeconds { get; set; } = 5;
}
