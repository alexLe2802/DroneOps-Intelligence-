namespace DroneOps.Application.Services.Missions;

// The airspace registry and FE classify these polygons as prohibited/restricted airspace.
public static class MissionRouteGeometry
{
    public record Point(double Latitude, double Longitude);
    private static double Cross(Point a, Point b, Point c) => (b.Longitude - a.Longitude) * (c.Latitude - a.Latitude) - (b.Latitude - a.Latitude) * (c.Longitude - a.Longitude);
    private static bool On(Point p, Point a, Point b) => Math.Abs(Cross(a, b, p)) < 1e-10 && p.Latitude >= Math.Min(a.Latitude, b.Latitude) - 1e-10 && p.Latitude <= Math.Max(a.Latitude, b.Latitude) + 1e-10 && p.Longitude >= Math.Min(a.Longitude, b.Longitude) - 1e-10 && p.Longitude <= Math.Max(a.Longitude, b.Longitude) + 1e-10;
    public static bool ValidPolygon(IReadOnlyList<Point> polygon)
    {
        var points = polygon.ToList();
        if (points.Count > 1 && points[0] == points[^1]) points.RemoveAt(points.Count - 1);
        if (points.Count < 3 || points.Distinct().Count() != points.Count) return false;
        double area = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i]; var b = points[(i + 1) % points.Count];
            if (Math.Abs(a.Longitude - b.Longitude) > 180) return false;
            area += a.Longitude * b.Latitude - b.Longitude * a.Latitude;
            for (var j = i + 1; j < points.Count; j++)
            {
                if (j == i + 1 || i == 0 && j == points.Count - 1) continue;
                var c = points[j]; var d = points[(j + 1) % points.Count];
                if (On(a, c, d) || On(b, c, d) || On(c, a, b) || On(d, a, b)
                    || Cross(a, b, c) * Cross(a, b, d) < 0 && Cross(c, d, a) * Cross(c, d, b) < 0) return false;
            }
        }
        return Math.Abs(area) > 1e-10;
    }

    public static bool BoundsOverlap(IReadOnlyList<Point> route, IReadOnlyList<Point> polygon)
    {
        if (route.Count == 0 || polygon.Count == 0) return false;
        return route.Max(x=>x.Latitude) >= polygon.Min(x=>x.Latitude)
            && route.Min(x=>x.Latitude) <= polygon.Max(x=>x.Latitude)
            && route.Max(x=>x.Longitude) >= polygon.Min(x=>x.Longitude)
            && route.Min(x=>x.Longitude) <= polygon.Max(x=>x.Longitude);
    }

    public static bool Intersects(IReadOnlyList<Point> route, IReadOnlyList<Point> polygon)
    {
        if (!BoundsOverlap(route, polygon)) return false;
        if (route.Any(x => Inside(x, polygon))) return true;
        for (int i = 1; i < route.Count; i++)
        {
            var a=route[i-1];var b=route[i];
            for (int j=0,k=polygon.Count-1;j<polygon.Count;k=j++)
            {
                var c=polygon[k];var d=polygon[j];
                if (On(a,c,d) || On(b,c,d) || On(c,a,b) || On(d,a,b)
                    || Cross(a,b,c)*Cross(a,b,d)<0 && Cross(c,d,a)*Cross(c,d,b)<0) return true;
            }
        }
        return false;
    }

    public static List<(double Start,double End)> InsideIntervals(Point a,Point b,IReadOnlyList<Point> polygon)
    {
        var cuts=new List<double>{0,1};var hits=new List<double>();
        var dx=b.Longitude-a.Longitude;var dy=b.Latitude-a.Latitude;
        double Parameter(Point p)=>Math.Abs(dx)>Math.Abs(dy)?(p.Longitude-a.Longitude)/dx:Math.Abs(dy)>1e-12?(p.Latitude-a.Latitude)/dy:0;
        for(int i=0,j=polygon.Count-1;i<polygon.Count;j=i++)
        {
            var c=polygon[j];var d=polygon[i];var ex=d.Longitude-c.Longitude;var ey=d.Latitude-c.Latitude;var det=dx*ey-dy*ex;
            if(Math.Abs(det)<1e-12){if(On(c,a,b))hits.Add(Parameter(c));if(On(d,a,b))hits.Add(Parameter(d));continue;}
            var t=((c.Longitude-a.Longitude)*ey-(c.Latitude-a.Latitude)*ex)/det;var u=((c.Longitude-a.Longitude)*dy-(c.Latitude-a.Latitude)*dx)/det;
            if(t>=0&&t<=1&&u>=0&&u<=1)hits.Add(t);
        }
        cuts.AddRange(hits);cuts=cuts.Distinct().Order().ToList();var result=hits.Select(t=>(t,t)).ToList();
        for(int i=1;i<cuts.Count;i++){var t=(cuts[i-1]+cuts[i])/2;if(Inside(new(a.Latitude+t*dy,a.Longitude+t*dx),polygon))result.Add((cuts[i-1],cuts[i]));}
        return result;
    }

    public static bool Inside(Point p, IReadOnlyList<Point> polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[j]; var b = polygon[i];
            if (On(p, a, b)) return true;
            if ((a.Latitude > p.Latitude) != (b.Latitude > p.Latitude) && p.Longitude < (b.Longitude - a.Longitude) * (p.Latitude - a.Latitude) / (b.Latitude - a.Latitude) + a.Longitude) inside = !inside;
        }
        return inside;
    }
}
