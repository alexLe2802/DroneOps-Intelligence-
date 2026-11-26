namespace DroneOps.Application.Services.AI;

public sealed record FlightFeature(Guid MissionId, string MissionName, double? Latitude, double? Longitude,
    double? DurationMinutes, double? AltitudeMeters, Guid? UavId, int? IncidentCount, IReadOnlyList<Guid> IncidentIds);

public static class HistoricalSimilarity
{
    public static IReadOnlyList<(FlightFeature Flight, double Score)> Rank(FlightFeature target, IEnumerable<FlightFeature> candidates, int maximum = 5)
    {
        var eligible = candidates.Where(x => x.MissionId != target.MissionId && HasMinimumEvidence(target) && HasMinimumEvidence(x)).ToList();
        var population = eligible.Append(target).ToList();
        var ranges = new[]
        {
            Range(population.Select(x => x.Latitude)), Range(population.Select(x => x.Longitude)),
            Range(population.Select(x => x.DurationMinutes)), Range(population.Select(x => x.AltitudeMeters)),
            Range(population.Select(x => (double?)x.IncidentCount))
        };

        return eligible.Select(candidate =>
        {
            var pairs = new[]
            {
                (target.Latitude, candidate.Latitude, ranges[0]), (target.Longitude, candidate.Longitude, ranges[1]),
                (target.DurationMinutes, candidate.DurationMinutes, ranges[2]), (target.AltitudeMeters, candidate.AltitudeMeters, ranges[3]),
                ((double?)target.IncidentCount, (double?)candidate.IncidentCount, ranges[4])
            };
            var usable = pairs.Where(x => x.Item1.HasValue && x.Item2.HasValue).ToList();
            var squared = usable.Sum(x => Math.Pow((x.Item1!.Value - x.Item2!.Value) / x.Item3, 2));
            var routeScore = usable.Count == 0 ? 0d : 1d / (1d + Math.Sqrt(squared / usable.Count));
            var score = target.UavId.HasValue && candidate.UavId.HasValue
                ? routeScore * 0.9d + (candidate.UavId == target.UavId ? 0.1d : 0d)
                : routeScore;
            return (Flight: candidate, Score: Math.Round(score, 6));
        }).OrderByDescending(x => x.Score).ThenBy(x => x.Flight.MissionId).Take(maximum).ToList();
    }

    // All three operational dimensions must be shared before incident count or UAV can influence ranking.
    private static bool HasMinimumEvidence(FlightFeature flight) =>
        flight.Latitude is >= -90 and <= 90 && flight.Longitude is >= -180 and <= 180
        && flight.DurationMinutes is > 0 && flight.AltitudeMeters.HasValue;

    private static double Range(IEnumerable<double?> values)
    {
        var materialized = values.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        return materialized.Count < 2 ? 1d : Math.Max(1e-9, materialized.Max() - materialized.Min());
    }
}
