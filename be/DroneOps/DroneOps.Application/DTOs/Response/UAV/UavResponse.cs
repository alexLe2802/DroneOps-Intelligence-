namespace DroneOps.Application.DTOs.Response.UAVs;

public sealed class UavResponse
{
    public Guid Id { get; set; }

    public Guid PilotId { get; set; }

    public string PilotName { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Model { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}