namespace DroneOps.Application.DTOs.Request.UAVs;

public sealed class CreateUavRequest
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Model { get; set; }
}