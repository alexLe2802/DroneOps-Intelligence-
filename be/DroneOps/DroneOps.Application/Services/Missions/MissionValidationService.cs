using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Application.Services.Missions;

public sealed class MissionValidationService(AppDbContext db, IMissionWeatherService weather)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    public static MissionValidationResult? Decode(MissionValidationRun? run) => run is null ? null : JsonSerializer.Deserialize<MissionValidationResult>(run.ResultJson, JsonOptions);
    public Task<MissionValidationRun?> LatestRunAsync(Guid id, CancellationToken ct) => db.MissionValidationRuns.AsNoTracking().Where(x=>x.MissionId==id && x.UpdateRequestId==null).OrderByDescending(x=>x.CheckedAt).FirstOrDefaultAsync(ct);
    public async Task<MissionValidationResult?> LatestAsync(Guid id,CancellationToken ct)=>Decode(await LatestRunAsync(id,ct));
    public async Task<MissionValidationResult?> LatestPreflightAsync(Guid id,CancellationToken ct)=>Decode(await db.MissionValidationRuns.AsNoTracking().Where(x=>x.MissionId==id&&x.Stage=="Preflight").OrderByDescending(x=>x.CheckedAt).FirstOrDefaultAsync(ct));
    public static string RestrictionHash(IEnumerable<Geofence> zones)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(zones.OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Revision,x.Coordinates,x.MinAltitudeMeters,x.MaxAltitudeMeters,x.AltitudeReference,x.VerticalRule}),JsonOptions))));
    public async Task<MissionValidationResult?> ReadAsync(Guid missionId,Guid actor,bool manager,Guid? requestId,CancellationToken ct)
    {
        var mission=await db.Missions.AsNoTracking().Include(x=>x.Waypoints).SingleOrDefaultAsync(x=>x.Id==missionId,ct)??throw new KeyNotFoundException("Mission not found.");
        if(!manager&&mission.CreatedBy!=actor)throw new UnauthorizedAccessException("Mission is outside your scope.");
        if(requestId.HasValue)
        {
            var request=await db.MissionUpdateRequests.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==requestId&&x.MissionId==missionId,ct)??throw new KeyNotFoundException("Update request not found.");
            mission=MissionPlans.Propose(mission,request);
        }
        var run=await db.MissionValidationRuns.AsNoTracking().Where(x=>x.MissionId==missionId&&x.UpdateRequestId==requestId).OrderByDescending(x=>x.CheckedAt).FirstOrDefaultAsync(ct);
        var result=Decode(run);
        if(result is not null)result.Stale=run!.PlanHash!=Hash(mission)||result.RestrictionRevision!=RestrictionHash(await db.Geofences.AsNoTracking().ToListAsync(ct));
        return result;
    }

    public async Task<MissionUpdateSummary?> PendingAsync(Guid id,CancellationToken ct)
    {
        var request=await db.MissionUpdateRequests.AsNoTracking().SingleOrDefaultAsync(x=>x.MissionId==id&&x.Status=="Pending",ct);
        if(request is null)return null;
        return new MissionUpdateSummary {Id=request.Id,Status=request.Status,IsInitial=request.IsInitial,CreatedAt=request.CreatedAt,ActorId=request.ActorId,BasePlan=request.BasePlanJson=="{}"?null:JsonSerializer.Deserialize<CreateMissionRequest>(request.BasePlanJson,JsonOptions),ProposedPlan=JsonSerializer.Deserialize<CreateMissionRequest>(request.ProposedPlanJson,JsonOptions)!,Validation=await ReadAsync(id,request.ActorId,false,request.Id,ct)};
    }
    public async Task<List<MissionUpdateSummary>> HistoryAsync(Guid id,Guid actor,bool manager,CancellationToken ct)
    {
        var owner=await db.Missions.AsNoTracking().Where(x=>x.Id==id).Select(x=>(Guid?)x.CreatedBy).SingleOrDefaultAsync(ct)??throw new KeyNotFoundException("Mission not found.");
        if(!manager&&owner!=actor)throw new UnauthorizedAccessException("Mission is outside your scope.");
        var requests=await db.MissionUpdateRequests.AsNoTracking().Where(x=>x.MissionId==id).OrderByDescending(x=>x.CreatedAt).ToListAsync(ct);
        var result=new List<MissionUpdateSummary>();
        foreach(var request in requests) result.Add(new(){Id=request.Id,Status=request.Status,IsInitial=request.IsInitial,CreatedAt=request.CreatedAt,ActorId=request.ActorId,ReviewedBy=request.ReviewedBy,ReviewedAt=request.ReviewedAt,Reason=request.Reason,BasePlan=request.BasePlanJson=="{}"?null:JsonSerializer.Deserialize<CreateMissionRequest>(request.BasePlanJson,JsonOptions),ProposedPlan=JsonSerializer.Deserialize<CreateMissionRequest>(request.ProposedPlanJson,JsonOptions)!,Validation=Decode(await db.MissionValidationRuns.AsNoTracking().Where(x=>x.UpdateRequestId==request.Id).OrderByDescending(x=>x.CheckedAt).FirstOrDefaultAsync(ct))});
        return result;
    }

    private static string? CanonicalTime(DateTime? value)=>value.HasValue?DateTime.SpecifyKind(value.Value,DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'",System.Globalization.CultureInfo.InvariantCulture):null;
    private static string? CanonicalNumber(decimal? value)=>value?.ToString("G29",System.Globalization.CultureInfo.InvariantCulture);
    public static object Snapshot(Mission m) => new {m.Id,m.CreatedBy,m.UAVId,m.Name,m.Description,m.AltitudeReference,StartTime=CanonicalTime(m.StartTime),EndTime=CanonicalTime(m.EndTime),waypoints=m.Waypoints.OrderBy(x=>x.SequenceOrder).Select(x=>new{x.SequenceOrder,Latitude=CanonicalNumber(x.Latitude),Longitude=CanonicalNumber(x.Longitude),Altitude=CanonicalNumber(x.Altitude),x.ActionType})};
    public static string Hash(Mission m) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Snapshot(m), JsonOptions))));
    public void Store(Mission m, Guid actor, MissionValidationResult result, bool persisted = true)
    {
        result.MissionId = persisted ? m.Id : null;
        db.MissionValidationRuns.Add(new MissionValidationRun { Id = result.Id, MissionId = result.MissionId, ActorId = actor, UpdateRequestId=result.UpdateRequestId, Stage = result.Stage, IsValid = result.IsValid, CheckedAt = result.CheckedAt, PlanHash = Hash(m), PlanJson = JsonSerializer.Serialize(Snapshot(m), JsonOptions), ResultJson = JsonSerializer.Serialize(result, JsonOptions) });
    }
    public async Task<MissionValidationResult> EvaluateAsync(Mission m, string stage, CancellationToken ct)
    {
        var r = new MissionValidationResult { MissionId = m.Id, Stage = stage };
        var dto = new CreateMissionRequest { Name = m.Name, Description = m.Description, AltitudeReference=m.AltitudeReference, UavId = m.UAVId ?? Guid.Empty, StartTime = m.StartTime ?? default, EndTime = m.EndTime ?? default, Waypoints = m.Waypoints.Select(x => new CreateWaypointRequest { SequenceOrder = x.SequenceOrder, Latitude = x.Latitude, Longitude = x.Longitude, Altitude = x.Altitude, ActionType = x.ActionType }).ToList() };
        var structural = await new CreateMissionRequestValidator().ValidateAsync(dto, ct);
        r.Issues.AddRange(structural.Errors.Select(x => x.ErrorMessage).Distinct());
        if (m.StartTime is null || m.EndTime is null) r.Issues.Add("A complete flight window is required.");
        else if ((stage is "Creation" or "Update" or "Manager" or "Approval") && m.StartTime <= DateTime.UtcNow) r.Issues.Add("Flight start time must be in the future.");
        else if (m.EndTime <= DateTime.UtcNow) r.Issues.Add("The planned flight window has ended.");
        var pilot = await db.Pilots.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == m.CreatedBy, ct);
        if (pilot is null) r.Issues.Add("Operator pilot profile is missing.");
        else if (string.IsNullOrWhiteSpace(pilot.PilotLicenseNo) || pilot.LicenseIssuedDate is null || pilot.LicenseExpiredDate is null || pilot.LicenseIssuedDate > DateOnly.FromDateTime(DateTime.UtcNow) || pilot.LicenseExpiredDate < DateOnly.FromDateTime(DateTime.UtcNow) || m.StartTime is not null && pilot.LicenseIssuedDate > DateOnly.FromDateTime(m.StartTime.Value) || m.EndTime is not null && pilot.LicenseExpiredDate < DateOnly.FromDateTime(m.EndTime.Value)) r.Issues.Add("Pilot license must be valid throughout the scheduled flight.");
        var uav = await db.UAVs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == m.UAVId, ct);
        if (uav is null || pilot is null || uav.PilotId != pilot.Id) r.Issues.Add("The UAV must belong to the mission operator.");
        if (uav is not null && !string.Equals(uav.Status, "Active", StringComparison.OrdinalIgnoreCase)) r.Issues.Add("The assigned UAV is not active.");
        if (await db.Missions.AsNoTracking().AnyAsync(x => x.Id != m.Id && (x.UAVId == m.UAVId || x.CreatedBy == m.CreatedBy) && (x.Status == "Approved" || x.Status == "InProgress" || x.Status == "In Progress") && x.StartTime < m.EndTime && x.EndTime > m.StartTime, ct)) r.Issues.Add("The UAV or operator has an overlapping approved/active mission.");
        var orderedPoints = m.Waypoints.OrderBy(x => x.SequenceOrder).ToList();
        if (orderedPoints.Zip(orderedPoints.Skip(1)).Any(x => Math.Abs(x.First.Longitude - x.Second.Longitude) > 180))
            r.Issues.Add("Routes crossing the antimeridian require an unsupported geospatial model.");
        var zones = await db.Geofences.AsNoTracking().ToListAsync(ct);
        r.RestrictionRevision=RestrictionHash(zones);
        var route = orderedPoints.Select(x => new MissionRouteGeometry.Point((double)x.Latitude, (double)x.Longitude)).ToList();
        if (zones.Count == 0) {r.Incomplete=true;r.Issues.Add("No airspace restriction data is configured.");}
        foreach (var zone in zones)
        {
            try
            {
                var polygon = JsonSerializer.Deserialize<List<MissionRouteGeometry.Point>>(zone.Coordinates, JsonOptions);
                if (polygon is null || polygon.Count < 3 || polygon.Any(x=>x is null || !double.IsFinite(x.Latitude)||!double.IsFinite(x.Longitude)||Math.Abs(x.Latitude)>90||Math.Abs(x.Longitude)>180))
                {
                    r.Incomplete=true;
                    r.Issues.Add($"Airspace restriction '{zone.Name}' has invalid coordinates and cannot be checked.");
                    continue;
                }
                // An unrelated remote polygon cannot affect this route. Validate topology
                // only when bounding boxes overlap, including route segments between nodes.
                if (!MissionRouteGeometry.BoundsOverlap(route,polygon)) continue;
                if (!MissionRouteGeometry.ValidPolygon(polygon))
                { r.Incomplete=true; r.Issues.Add($"Airspace restriction '{zone.Name}' overlaps the route area but its geometry is invalid. Review the restriction data."); }
                else
                {
                    var bounded=zone.MinAltitudeMeters.HasValue||zone.MaxAltitudeMeters.HasValue;
                    if(zone.MinAltitudeMeters>zone.MaxAltitudeMeters){r.Incomplete=true;r.Issues.Add($"Altitude rule for '{zone.Name}' is invalid.");continue;}
                    bool unknown=(!bounded&&zone.VerticalRule!="AllAltitudes")||bounded&&(!(zone.AltitudeReference is "AGL" or "MSL" or "Relative")||string.IsNullOrWhiteSpace(zone.AltitudeReference)||string.IsNullOrWhiteSpace(m.AltitudeReference)||zone.AltitudeReference!=m.AltitudeReference);
                    void Finding(int? point,int? from,int? to,string reason,bool incomplete)
                    {
                        r.Issues.Add(reason);r.Incomplete|=incomplete;
                        r.Findings.Add(new RestrictionFinding {GeofenceId=zone.Id,Revision=zone.Revision,WaypointSequence=point,SegmentFrom=from,SegmentTo=to,Rule=incomplete?"AltitudeReferenceIncomplete":"AirspaceAltitudeIntersection",Reason=reason});
                    }
                    bool Vertical(decimal? low,decimal? high)=>!bounded || low.HasValue&&high.HasValue&&high>=(zone.MinAltitudeMeters??decimal.MinValue)&&low<=(zone.MaxAltitudeMeters??decimal.MaxValue);
                    foreach(var point in orderedPoints.Where(x=>MissionRouteGeometry.Inside(new((double)x.Latitude,(double)x.Longitude),polygon)))
                    {
                        if(unknown||bounded&&!point.Altitude.HasValue) Finding(point.SequenceOrder,null,null,$"Cannot determine altitude clearance at waypoint {point.SequenceOrder} for '{zone.Name}': altitude/reference missing or incompatible.",true);
                        else if(Vertical(point.Altitude,point.Altitude)) Finding(point.SequenceOrder,null,null,$"Waypoint {point.SequenceOrder} violates airspace restriction '{zone.Name}'.",false);
                    }
                    for(int i=1;i<orderedPoints.Count;i++)
                    {
                        var first=orderedPoints[i-1];var last=orderedPoints[i];
                        var intervals=MissionRouteGeometry.InsideIntervals(new((double)first.Latitude,(double)first.Longitude),new((double)last.Latitude,(double)last.Longitude),polygon);
                        if(intervals.Count==0)continue;
                        if(unknown||bounded&&(!first.Altitude.HasValue||!last.Altitude.HasValue)) Finding(null,first.SequenceOrder,last.SequenceOrder,$"Cannot determine altitude clearance for segment {first.SequenceOrder}-{last.SequenceOrder} at '{zone.Name}'.",true);
                        else if(intervals.Any(t=>
                        {
                            decimal? a=first.Altitude+(last.Altitude-first.Altitude)*(decimal)t.Start;
                            decimal? b=first.Altitude+(last.Altitude-first.Altitude)*(decimal)t.End;
                            return Vertical(a.HasValue&&b.HasValue?Math.Min(a.Value,b.Value):null,a.HasValue&&b.HasValue?Math.Max(a.Value,b.Value):null);
                        })) Finding(null,first.SequenceOrder,last.SequenceOrder,$"Segment {first.SequenceOrder}-{last.SequenceOrder} violates airspace restriction '{zone.Name}'.",false);
                    }
                }
            }
            catch (JsonException) { r.Incomplete=true; r.Issues.Add($"Airspace restriction '{zone.Name}' could not be checked."); }
        }
        r.Weather = await weather.GetAsync(m, ct);
        r.Warnings.AddRange(r.Weather.Warnings);
        return r;
    }
    public async Task<MissionValidationResult> RevalidateAsync(Guid id, Guid actor, bool manager, string stage, CancellationToken ct, Guid? updateRequestId=null)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var m = await db.Missions.Include(x => x.Waypoints).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Mission not found.");
            if (!manager && m.CreatedBy != actor) throw new UnauthorizedAccessException("You may only validate your own mission.");
            MissionUpdateRequest? update=null;
            if(updateRequestId.HasValue)
            {
                update=await db.MissionUpdateRequests.SingleOrDefaultAsync(x=>x.Id==updateRequestId&&x.MissionId==id,ct)??throw new KeyNotFoundException("Update request not found.");
                if(update.Status!="Pending")throw new InvalidOperationException("Only pending requests can be revalidated.");
                m=MissionPlans.Propose(m,update);
            }
            if (stage == "Preflight")
            {
                if (m.Status != "Approved") throw new InvalidOperationException("Preflight checks require an approved mission.");
                var recent = await db.MissionValidationRuns.AsNoTracking().Where(x => x.MissionId == id && x.Stage == "Preflight" && x.CheckedAt > DateTimeOffset.UtcNow.AddMinutes(-1)).OrderByDescending(x => x.CheckedAt).FirstOrDefaultAsync(ct);
                if (recent is not null && recent.PlanHash == Hash(m) && Decode(recent)?.RestrictionRevision == RestrictionHash(await db.Geofences.AsNoTracking().ToListAsync(ct))) { await tx.CommitAsync(ct); return Decode(recent)!; }
            }
            var r = await EvaluateAsync(m, stage, ct); r.UpdateRequestId=update?.Id; Store(m, actor, r);
            if(update is not null)update.ValidationRunId=r.Id;
            if (stage == "Preflight") await SyncPreflightAlertAsync(id, r, ct);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return r;
        });
    }
    public async Task SyncPreflightAlertAsync(Guid id, MissionValidationResult r, CancellationToken ct)
    {

        var existing = await db.Incidents.FirstOrDefaultAsync(x => x.MissionId == id && x.Type == "PreflightValidation" && x.Status == "Open", ct);
        if ((!r.IsValid || r.Warnings.Count > 0) && existing is null) db.Incidents.Add(new Incident { Id = Guid.NewGuid(), MissionId = id, Title = r.IsValid ? "Preflight weather advisory" : "Preflight blocked", Description = r.Message + " " + string.Join(" ", r.Warnings), Type = "PreflightValidation", Severity = r.IsValid ? "Medium" : "High", Status = "Open", CreatedAt = DateTimeOffset.UtcNow, Evidence = JsonSerializer.Serialize(r, JsonOptions) });
        else if (existing is not null)
        {
            if (r.IsValid && r.Warnings.Count == 0) { existing.Status = "Resolved"; existing.ResolvedAt = DateTimeOffset.UtcNow; existing.ResolutionNotes = "A later preflight check passed without warnings."; }
            else { existing.Title = r.IsValid ? "Preflight weather advisory" : "Preflight blocked"; existing.Severity = r.IsValid ? "Medium" : "High"; existing.Description = r.Message + " " + string.Join(" ", r.Warnings); existing.Evidence = JsonSerializer.Serialize(r, JsonOptions); }
        }
    }

}
