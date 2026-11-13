using DroneOps.Application.Services.Missions;
using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;

internal static class MissionFenceChecks
{
    public static void Run()
    {
        var polygon=new MissionFence("polygon","Polygon",[new(10,106),new(10,107),new(11,107),new(11,106)]);
        var circle=new MissionFence("circle","Circle",Center:new(12,108),RadiusMeters:100);
        Check(MissionFenceGeometry.Valid(polygon)&&MissionFenceGeometry.Valid(circle),"both mission-fence shapes validate");
        Check(MissionFenceGeometry.Contains(polygon,10.5m,106.5m),"polygon interior is permitted");
        Check(MissionFenceGeometry.Contains(polygon,10m,106.5m),"polygon boundary is inside");
        Check(!MissionFenceGeometry.Contains(polygon,9m,106.5m),"polygon exterior is outside");
        Check(MissionFenceGeometry.Contains(circle,12,108)&&!MissionFenceGeometry.Contains(circle,12.01m,108),"circle uses radius in metres");
        Check(MissionFenceGeometry.InsideAny([polygon,circle],12,108),"multiple permitted fences form a union");
        Check(!MissionFenceGeometry.Valid(polygon with {Vertices=[new(10,106),new(11,107),new(10,107),new(11,106)]}),"self-intersecting polygon is rejected");
        Check(!MissionFenceGeometry.Valid(circle with {RadiusMeters=0}),"zero-radius circle is rejected");
        var mission=new Mission {Waypoints=[new Waypoint{SequenceOrder=1,Latitude=10,Longitude=106}]};
        var before=MissionValidationService.Hash(mission);
        mission.Waypoints.First().Navigation=new(MissionFences:[circle]);
        Check(before!=MissionValidationService.Hash(mission),"manual mission fence changes invalidate approval hash too");
    }
    static void Check(bool value,string message) {if(!value)throw new InvalidOperationException("FAIL: "+message);Console.WriteLine("PASS: "+message);}
}
