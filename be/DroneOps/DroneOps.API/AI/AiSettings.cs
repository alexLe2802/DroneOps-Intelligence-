namespace DroneOps.API.AI;

public sealed class AiSettings
{
    public const string SectionName = "Ai";
    public string Provider { get; set; } = "Gemini";
    public string Model { get; set; } = "gemini-3.5-flash-lite";
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
}
