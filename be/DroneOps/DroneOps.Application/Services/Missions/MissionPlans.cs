using System.Text.Json;
using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Domain.Entities;
namespace DroneOps.Application.Services.Missions;

public static class MissionPlans
{
    private static DateTime StoredTime(DateTime value) { var utc=value.ToUniversalTime();return new DateTime(utc.Ticks/10*10,DateTimeKind.Utc); }
    public static Mission Propose(Mission current, CreateMissionRequest dto) => new()
    {
        Id=current.Id, CreatedBy=current.CreatedBy, CreatedAt=current.CreatedAt, Status=current.Status,
        UAVId=dto.UavId,Name=dto.Name.Trim(),Description=dto.Description,
        AltitudeReference=dto.AltitudeReference,StartTime=StoredTime(dto.StartTime),EndTime=StoredTime(dto.EndTime),
        Waypoints=dto.Waypoints.Select(x=>new Waypoint {Id=Guid.NewGuid(),MissionId=current.Id,SequenceOrder=x.SequenceOrder,Latitude=decimal.Round(x.Latitude,7,MidpointRounding.AwayFromZero),Longitude=decimal.Round(x.Longitude,7,MidpointRounding.AwayFromZero),Altitude=x.Altitude.HasValue?decimal.Round(x.Altitude.Value,2,MidpointRounding.AwayFromZero):null,ActionType=x.ActionType??"FlyThrough",CreatedAt=DateTime.UtcNow}).ToList()
    };
    public static Mission Propose(Mission current, MissionUpdateRequest request) => Propose(current,JsonSerializer.Deserialize<CreateMissionRequest>(request.ProposedPlanJson,MissionValidationService.JsonOptions)??throw new InvalidOperationException("Proposed plan is missing."));
    public static CreateMissionRequest Dto(Mission m) => new() {UavId=m.UAVId??Guid.Empty,Name=m.Name,Description=m.Description,AltitudeReference=m.AltitudeReference,StartTime=m.StartTime??default,EndTime=m.EndTime??default,Waypoints=m.Waypoints.OrderBy(x=>x.SequenceOrder).Select(x=>new CreateWaypointRequest {SequenceOrder=x.SequenceOrder,Latitude=decimal.Round(x.Latitude,7,MidpointRounding.AwayFromZero),Longitude=decimal.Round(x.Longitude,7,MidpointRounding.AwayFromZero),Altitude=x.Altitude.HasValue?decimal.Round(x.Altitude.Value,2,MidpointRounding.AwayFromZero):null,ActionType=x.ActionType}).ToList()};
}
