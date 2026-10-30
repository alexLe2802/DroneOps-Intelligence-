using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DroneOps.Application.DTOs.Request.AI;
using DroneOps.Application.DTOs.Response.AI;
using DroneOps.Application.Interfaces.AI;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Application.Services.AI;

public sealed class AiAssessmentService(AppDbContext db) : IAiAssessmentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AiAssessmentResponse> CreateAsync(CreateAiAssessmentRequest request, Guid userId, bool isManager, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.MissionRef, out var missionId)) throw new ArgumentException("Mission reference must be a UUID.");
        if (request.MissionVersion < 1) throw new ArgumentException("Mission version must be positive.");
        var mission = await AuthorizedMissions(userId, isManager).Include(x => x.UAV)
            .SingleOrDefaultAsync(x => x.Id == missionId, cancellationToken)
            ?? throw new KeyNotFoundException("Mission was not found in the authorized scope.");

        var candidates = await AuthorizedMissions(userId, isManager)
            .Where(x => x.Id != missionId && x.Status == "Completed")
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        var ids = candidates.Select(x => x.Id).Append(missionId).ToList();
        var telemetry = await db.TelemetryRecords.AsNoTracking().Where(x => x.MissionId.HasValue && ids.Contains(x.MissionId.Value) && x.IsValid).ToListAsync(cancellationToken);
        var incidents = await db.Incidents.AsNoTracking().Where(x => ids.Contains(x.MissionId)).ToListAsync(cancellationToken);

        FlightFeature Feature(Mission value)
        {
            var points = telemetry.Where(x => x.MissionId == value.Id).ToList();
            var eventIds = incidents.Where(x => x.MissionId == value.Id).Select(x => x.Id).Order().ToList();
            double? Average(Func<TelemetryRecord, decimal?> selector)
            {
                var values = points.Select(selector).Where(x => x.HasValue).Select(x => (double)x!.Value).ToList();
                return values.Count == 0 ? null : values.Average();
            }
            var duration = value.StartTime.HasValue && value.EndTime.HasValue ? (value.EndTime.Value - value.StartTime.Value).TotalMinutes : (double?)null;
            return new(value.Id, value.Name, Average(x => x.Latitude), Average(x => x.Longitude), duration,
                Average(x => x.Altitude), value.UAVId, eventIds.Count, eventIds);
        }

        var target = Feature(mission);
        var ranked = HistoricalSimilarity.Rank(target, candidates.Select(Feature));
        var similar = ranked.Select(x => new SimilarFlightResponse
        {
            MissionId = x.Flight.MissionId, MissionName = x.Flight.MissionName, SimilarityScore = x.Score,
            IncidentIds = x.Flight.IncidentIds.ToList(), Features = FeatureDictionary(x.Flight)
        }).ToList();
        var missing = new List<string>();
        if (target.Latitude is null || target.Longitude is null) missing.Add("Target mission has no valid GPS centroid.");
        if (target.AltitudeMeters is null) missing.Add("Target mission has no valid recorded altitude.");
        if (target.DurationMinutes is null) missing.Add("Target mission has no trusted start/end duration.");
        if (similar.Count == 0) missing.Add("No similar completed flights are available in the authorized scope.");
        var risks = similar.Where(x => x.IncidentIds.Count > 0).Select(x => new AiRiskResponse
        {
            Severity = "Moderate", Title = $"Historical incidents on {x.MissionName}",
            Evidence = $"{x.IncidentIds.Count} linked incident(s); similarity score {x.SimilarityScore:F3}.",
            Precaution = "Review the linked incident evidence before operational approval."
        }).ToList();
        var result = new AiAssessmentResultResponse
        {
            Summary = similar.Count == 0 ? "No eligible historical flight was found; manual review is required." : $"Retrieved {similar.Count} authorized completed flight(s) using recorded normalized features.",
            Risks = risks, MissingData = missing, HistoricalFlights = similar,
            Precautions = risks.Count == 0 ? ["Continue mandatory validation and manual operational review."] : ["Open every linked incident before approval.", "Do not treat similarity as a probability of safety."],
            Disclaimer = "Historical similarity is explainable decision support, not a safety probability and not an approval decision."
        };
        var context = new { targetMissionId = mission.Id, target = FeatureDictionary(target), retrieval = result.Retrieval, sourceMissionIds = similar.Select(x => x.MissionId), sourceIncidentIds = similar.SelectMany(x => x.IncidentIds) };
        var contextJson = JsonSerializer.Serialize(context, JsonOptions);
        var now = DateTimeOffset.UtcNow;
        var entity = new AiAssessment
        {
            Id = Guid.NewGuid(), MissionRef = mission.Id.ToString(), MissionVersion = request.MissionVersion, RequestedBy = userId,
            Provider = "historical-evidence", Model = "normalized-euclidean-v1", ContextHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contextJson))).ToLowerInvariant(),
            ContextSnapshot = contextJson, Status = "Available", Result = JsonSerializer.Serialize(result, JsonOptions), CreatedAt = now, CompletedAt = now
        };
        db.AiAssessments.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<List<AiAssessmentResponse>> GetByMissionAsync(string missionRef, Guid userId, bool isManager, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(missionRef, out var missionId)) throw new ArgumentException("Mission reference must be a UUID.");
        if (!await AuthorizedMissions(userId, isManager).AnyAsync(x => x.Id == missionId, cancellationToken))
            throw new KeyNotFoundException("Mission was not found in the authorized scope.");
        return (await db.AiAssessments.AsNoTracking().Where(x => x.MissionRef == missionId.ToString())
            .OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken)).Select(Map).ToList();
    }

    private IQueryable<Mission> AuthorizedMissions(Guid userId, bool isManager) =>
        isManager ? db.Missions.AsNoTracking() : db.Missions.AsNoTracking().Where(x => x.CreatedBy == userId);

    private static Dictionary<string, double?> FeatureDictionary(FlightFeature value) => new()
    {
        ["centroidLatitude"] = value.Latitude, ["centroidLongitude"] = value.Longitude,
        ["durationMinutes"] = value.DurationMinutes, ["meanAltitudeMeters"] = value.AltitudeMeters,
        ["incidentCount"] = value.IncidentCount
    };

    private static AiAssessmentResponse Map(AiAssessment value) => new()
    {
        Id = value.Id, MissionRef = value.MissionRef, MissionVersion = value.MissionVersion, Provider = value.Provider,
        Model = value.Model, Status = value.Status, ErrorCode = value.ErrorCode, CreatedAt = value.CreatedAt,
        CompletedAt = value.CompletedAt, Result = string.IsNullOrWhiteSpace(value.Result) ? null : JsonSerializer.Deserialize<AiAssessmentResultResponse>(value.Result, JsonOptions)
    };
}
