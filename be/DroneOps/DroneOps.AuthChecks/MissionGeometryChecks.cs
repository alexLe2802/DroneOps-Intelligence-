using System.Text.Json;
using DroneOps.Application.Services.Missions;
using P = DroneOps.Application.Services.Missions.MissionRouteGeometry.Point;

internal static class MissionGeometryChecks
{
    public static void Run(string? fixture)
    {
        var count = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("FAIL: " + message);
            Console.WriteLine("PASS: " + message); count++;
        }
        // Concave L: the route occupies its empty notch despite overlapping bounds.
        P[] concave = [new(0,0),new(0,4),new(1,4),new(1,1),new(4,1),new(4,0)];
        P[] outside = [new(2,2),new(3,3)];
        Check(MissionRouteGeometry.BoundsOverlap(outside,concave), "regression has overlapping bounding boxes");
        Check(!MissionRouteGeometry.Intersects(outside,concave), "empty area within restriction bounds is not restricted");
        P[] invalid = [new(0,0),new(4,4),new(0,4),new(4,0)];
        Check(!MissionRouteGeometry.ValidPolygon(invalid), "self-intersecting polygon remains invalid");
        Check(!MissionRouteGeometry.Intersects([new(0.1,1.9),new(0.1,2.1)],invalid), "unfilled area does not inherit a distant topology defect");
        Check(MissionRouteGeometry.Intersects([new(2,.2),new(2,.4)],invalid), "filled area of invalid polygon still requires data review");
        P[] square = [new(0,0),new(0,4),new(4,4),new(4,0),new(0,0)];
        Check(MissionRouteGeometry.Intersects([new(2,-1),new(2,5)],square), "crossing segment blocks even when both endpoints are outside");
        Check(MissionRouteGeometry.Intersects([new(0,-1),new(0,0)],square), "boundary touch is detected");
        Check(MissionRouteGeometry.Intersects([new(2,2)],square), "inside waypoint is detected");
        Check(!MissionRouteGeometry.Intersects([new(5,1),new(5,3)],square), "outside route remains clear");
        if (fixture is not null)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(fixture));
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var polygon = doc.RootElement.GetProperty("polygon").Deserialize<List<P>>(options)!;
            var route = doc.RootElement.GetProperty("route").Deserialize<List<P>>(options)!;
            Check(MissionRouteGeometry.BoundsOverlap(route,polygon), "actual mission overlaps the large restriction bounding box");
            Check(!MissionRouteGeometry.Intersects(route,polygon), $"actual mission's {route.Count} waypoints and segments are outside the {polygon.Count}-vertex restriction");
        }
        Console.WriteLine($"{count} geometry checks passed.");
    }
}
