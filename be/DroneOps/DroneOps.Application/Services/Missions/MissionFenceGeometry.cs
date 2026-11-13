using DroneOps.Domain.Entities;

namespace DroneOps.Application.Services.Missions;

public static class MissionFenceGeometry
{
    public static bool Coordinate(FenceCoordinate p) => p.Latitude is >= -90 and <= 90 && p.Longitude is >= -180 and <= 180;
    public static bool Valid(MissionFence f) => f != null && !string.IsNullOrWhiteSpace(f.Id) && f.Id.Length <= 64 && (f.Shape switch {
        "Circle" => f.Center != null && Coordinate(f.Center) && f.RadiusMeters is >= 1 and <= 100000 && f.Vertices == null,
        "Polygon" => f.Center == null && f.RadiusMeters == null && f.Vertices is { Count: >= 3 and <= 100 } v
            && v.All(p => p != null && Coordinate(p)) && v.Distinct().Count() == v.Count
            && v.Max(p => p.Longitude) - v.Min(p => p.Longitude) < 180
            && Math.Abs(Area(v)) > 0.000000000001m && !SelfIntersects(v),
        _ => false
    });

    private static decimal Area(IReadOnlyList<FenceCoordinate> v) => v.Select((p, i) => p.Longitude * v[(i + 1) % v.Count].Latitude - v[(i + 1) % v.Count].Longitude * p.Latitude).Sum() / 2;
    private static decimal Cross(FenceCoordinate a, FenceCoordinate b, FenceCoordinate p) => (b.Longitude-a.Longitude)*(p.Latitude-a.Latitude)-(b.Latitude-a.Latitude)*(p.Longitude-a.Longitude);
    private static bool On(FenceCoordinate a, FenceCoordinate b, FenceCoordinate p) => Math.Abs(Cross(a,b,p)) <= 0.000000000001m
        && p.Latitude >= Math.Min(a.Latitude,b.Latitude) && p.Latitude <= Math.Max(a.Latitude,b.Latitude)
        && p.Longitude >= Math.Min(a.Longitude,b.Longitude) && p.Longitude <= Math.Max(a.Longitude,b.Longitude);
    private static bool SelfIntersects(IReadOnlyList<FenceCoordinate> v)
    {
        for (var i=0;i<v.Count;i++) for(var j=i+1;j<v.Count;j++)
        {
            if(j==i+1 || i==0 && j==v.Count-1) continue;
            var a=v[i];var b=v[(i+1)%v.Count];var c=v[j];var d=v[(j+1)%v.Count];
            if (On(a,b,c)||On(a,b,d)||On(c,d,a)||On(c,d,b)
                || Math.Sign(Cross(a,b,c))*Math.Sign(Cross(a,b,d))<0 && Math.Sign(Cross(c,d,a))*Math.Sign(Cross(c,d,b))<0) return true;
        }
        return false;
    }
    public static bool Contains(MissionFence f, decimal latitude, decimal longitude)
    {
        if (!Valid(f) || !Coordinate(new(latitude,longitude))) return false;
        if (f.Shape == "Circle")
        {
            static double R(decimal x) => (double)x * Math.PI / 180;
            var c=f.Center!;
            var a=Math.Pow(Math.Sin(R(latitude-c.Latitude)/2),2)+Math.Cos(R(latitude))*Math.Cos(R(c.Latitude))*Math.Pow(Math.Sin(R(longitude-c.Longitude)/2),2);
            return 6371000*2*Math.Asin(Math.Sqrt(Math.Clamp(a,0,1))) <= (double)f.RadiusMeters!.Value + 0.01;
        }
        var v=f.Vertices!;var p=new FenceCoordinate(latitude,longitude);var inside=false;
        for(var i=0;i<v.Count;i++)
        {
            var a=v[i];var b=v[(i+1)%v.Count];
            if(On(a,b,p)) return true;
            if((a.Latitude>latitude)!=(b.Latitude>latitude) && longitude<(b.Longitude-a.Longitude)*(latitude-a.Latitude)/(b.Latitude-a.Latitude)+a.Longitude) inside=!inside;
        }
        return inside;
    }
    public static bool InsideAny(IReadOnlyList<MissionFence> fences, decimal latitude, decimal longitude) => fences.Any(f=>Contains(f,latitude,longitude));
}
