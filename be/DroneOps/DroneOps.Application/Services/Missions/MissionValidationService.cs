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

public sealed class MissionValidationService
{
    private readonly AppDbContext db;
    private readonly IMissionWeatherService weather;
    private readonly ITerrainElevationService terrain;

    public MissionValidationService(AppDbContext db, IMissionWeatherService weather)
        : this(db, weather, new UnavailableTerrainElevationService()) { }

    public MissionValidationService(AppDbContext db, IMissionWeatherService weather, ITerrainElevationService terrain)
    {
        this.db = db;
        this.weather = weather;
        this.terrain = terrain;
    }
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    public static MissionValidationResult? Decode(MissionValidationRun? run) => run is null ? null : JsonSerializer.Deserialize<MissionValidationResult>(run.ResultJson, JsonOptions);
    public Task<MissionValidationRun?> LatestRunAsync(Guid id, CancellationToken ct) => db.MissionValidationRuns.AsNoTracking().Where(x=>x.MissionId==id && x.UpdateRequestId==null).OrderByDescending(x=>x.CheckedAt).FirstOrDefaultAsync(ct);
    public async Task<MissionValidationResult?> LatestAsync(Guid id,CancellationToken ct)=>Decode(await LatestRunAsync(id,ct));
    public async Task<MissionValidationResult?> LatestPreflightAsync(Guid id,CancellationToken ct)=>Decode(await db.MissionValidationRuns.AsNoTracking().Where(x=>x.MissionId==id&&x.Stage=="Preflight").OrderByDescending(x=>x.CheckedAt).FirstOrDefaultAsync(ct));
    public static string RestrictionHash(IEnumerable<Geofence> zones)=>HashRestrictionRevisions(zones.Select(x=>(x.Id,x.Revision)));
    private static string HashRestrictionRevisions(IEnumerable<(Guid Id,long Revision)> zones)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(zones.OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Revision}),JsonOptions))));
    private async Task<string> CurrentRestrictionHashAsync(CancellationToken ct)
    {
        var revisions=await db.Geofences.AsNoTracking().Select(x=>new{x.Id,x.Revision}).ToListAsync(ct);
        return HashRestrictionRevisions(revisions.Select(x=>(x.Id,x.Revision)));
    }
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
        if(result is not null)result.Stale=run!.PlanHash!=Hash(mission)||result.RestrictionRevision!=await CurrentRestrictionHashAsync(ct);
        return result;
    }

    public async Task<MissionValidationResult?> ReadLatestRunAsync(Guid missionId,Guid actor,bool manager,CancellationToken ct)
    {
        var mission=await db.Missions.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==missionId,ct)??throw new KeyNotFoundException("Mission not found.");
        if(!manager&&mission.CreatedBy!=actor)throw new UnauthorizedAccessException("Mission is outside your scope.");
        return Decode(await db.MissionValidationRuns.AsNoTracking().Where(x=>x.MissionId==missionId)
            .OrderByDescending(x=>x.CheckedAt).ThenByDescending(x=>x.Id).FirstOrDefaultAsync(ct));
    }

    public async Task<MissionValidationResult?> ReadRunAsync(Guid missionId,Guid runId,Guid actor,bool manager,CancellationToken ct)
    {
        var mission=await db.Missions.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==missionId,ct)??throw new KeyNotFoundException("Mission not found.");
        if(!manager&&mission.CreatedBy!=actor)throw new UnauthorizedAccessException("Mission is outside your scope.");
        var run=await db.MissionValidationRuns.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==runId&&x.MissionId==missionId,ct)??throw new KeyNotFoundException("Validation result not found.");
        return Decode(run);
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
        var planHash=Hash(m);
        var planJson=JsonSerializer.Serialize(Snapshot(m), JsonOptions);
        db.MissionValidationRuns.Add(new MissionValidationRun
        {
            Id=result.Id, MissionId=result.MissionId, ActorId=actor, UpdateRequestId=result.UpdateRequestId,
            Stage=result.Stage, IsValid=result.IsValid, ResultStatus=result.Outcome, CheckedAt=result.CheckedAt,
            PlanHash=planHash, RestrictionRevision=result.RestrictionRevision, PlanJson=planJson,
            EvidenceJson=JsonSerializer.Serialize(new
            {
                planHash,
                plan=JsonSerializer.Deserialize<JsonElement>(planJson),
                findings=result.Findings,
                restrictions=result.RestrictionEvidence
            },JsonOptions),
            ResultJson=JsonSerializer.Serialize(result,JsonOptions)
        });
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
        var operationalBoundaries=zones.Where(x=>x.RestrictionType=="OperationalBoundary").ToList();
        if(route.Count>0&&operationalBoundaries.Count>0)
        {
            var containing=new List<(Geofence Zone,List<MissionRouteGeometry.Point> Polygon)>();
            foreach(var boundary in operationalBoundaries)
            {
                try
                {
                    var polygon=JsonSerializer.Deserialize<List<MissionRouteGeometry.Point>>(boundary.Coordinates,JsonOptions);
                    if(polygon is null||!MissionRouteGeometry.ValidPolygon(polygon))
                    {r.Incomplete=true;r.Issues.Add($"Operational boundary '{boundary.Name}' has invalid geometry.");continue;}
                    r.RestrictionEvidence.Add(Evidence(boundary,$"Operational boundary '{boundary.Name}' revision {boundary.Revision} was evaluated as keep-in airspace."));
                    if(CoversRoute(route,polygon))containing.Add((boundary,polygon));
                }
                catch(JsonException){r.Incomplete=true;r.Issues.Add($"Operational boundary '{boundary.Name}' could not be checked.");}
            }
            if(containing.Count==0)
            {
                var boundary=operationalBoundaries[0];
                var reason="The route leaves every configured operational boundary.";
                r.Issues.Add(reason);r.Findings.Add(new RestrictionFinding {GeofenceId=boundary.Id,Revision=boundary.Revision,Rule="OutsideOperationalBoundary",Reason=reason});
            }
            else
            {
                var altitudePass=false;var altitudeIncomplete=false;
                foreach(var candidate in containing)
                {
                    var zone=candidate.Zone;var bounded=zone.MinAltitudeMeters.HasValue||zone.MaxAltitudeMeters.HasValue;
                    if(!bounded&&zone.VerticalRule=="AllAltitudes"){altitudePass=true;break;}
                    if(!bounded||!(zone.AltitudeReference is "AGL" or "MSL")||!(m.AltitudeReference is "AGL" or "MSL"))
                    {altitudeIncomplete=true;continue;}
                    var converted=new List<decimal?>();
                    foreach(var point in orderedPoints)converted.Add(await ConvertAltitudeAsync(point.Altitude,point.Latitude,point.Longitude,m.AltitudeReference,zone.AltitudeReference,ct));
                    if(converted.Any(x=>!x.HasValue)){altitudeIncomplete=true;continue;}
                    if(converted.All(x=>x>=(zone.MinAltitudeMeters??decimal.MinValue)&&x<=(zone.MaxAltitudeMeters??decimal.MaxValue))){altitudePass=true;break;}
                }
                if(!altitudePass)
                {
                    var boundary=containing[0].Zone;
                    var reason=altitudeIncomplete?"Operational-boundary altitude clearance is incomplete because terrain, altitude, or reference data is unavailable.":"The route leaves the operational boundary altitude band.";
                    r.Incomplete|=altitudeIncomplete;r.Issues.Add(reason);r.Findings.Add(new RestrictionFinding {GeofenceId=boundary.Id,Revision=boundary.Revision,Rule=altitudeIncomplete?"OperationalAltitudeIncomplete":"OutsideOperationalAltitude",Reason=reason});
                }
            }
        }
        foreach (var zone in zones.Where(x=>x.RestrictionType!="OperationalBoundary"))
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
                // A bounding-box overlap is only a broad-phase candidate. Check the
                // actual boundary and even-odd filled area (the map's fill rule) before
                // allowing unrelated topology defects to block this route. Both nodes
                // and intervening segments, including boundary touches, are checked.
                if (!MissionRouteGeometry.Intersects(route,polygon)) continue;
                r.RestrictionEvidence.Add(new RestrictionEvidence
                {
                    GeofenceId=zone.Id, Revision=zone.Revision, Name=zone.Name, RestrictionType=zone.RestrictionType,
                    CoordinatesJson=zone.Coordinates, VerticalRule=zone.VerticalRule,
                    AltitudeReference=zone.AltitudeReference, MinAltitudeMeters=zone.MinAltitudeMeters,
                    MaxAltitudeMeters=zone.MaxAltitudeMeters,
                    Text=$"Route intersects restriction '{zone.Name}' revision {zone.Revision}."
                });
                if (!MissionRouteGeometry.ValidPolygon(polygon))
                { r.Incomplete=true; r.Issues.Add($"Airspace restriction '{zone.Name}' intersects the submitted route but its geometry is invalid. Clearance is incomplete; review the restriction data."); }
                else
                {
                    var bounded=zone.MinAltitudeMeters.HasValue||zone.MaxAltitudeMeters.HasValue;
                    if(zone.MinAltitudeMeters>zone.MaxAltitudeMeters){r.Incomplete=true;r.Issues.Add($"Altitude rule for '{zone.Name}' is invalid.");continue;}
                    bool unknown=!bounded&&zone.VerticalRule!="AllAltitudes" ||
                        bounded&&(!(zone.AltitudeReference is "AGL" or "MSL") || !(m.AltitudeReference is "AGL" or "MSL"));
                    void Finding(int? point,int? from,int? to,string reason,bool incomplete)
                    {
                        r.Issues.Add(reason);r.Incomplete|=incomplete;
                        r.Findings.Add(new RestrictionFinding {GeofenceId=zone.Id,Revision=zone.Revision,WaypointSequence=point,SegmentFrom=from,SegmentTo=to,Rule=incomplete?"AltitudeReferenceIncomplete":"AirspaceAltitudeIntersection",Reason=reason});
                    }
                    bool Vertical(decimal? low,decimal? high)=>!bounded || low.HasValue&&high.HasValue&&high>=(zone.MinAltitudeMeters??decimal.MinValue)&&low<=(zone.MaxAltitudeMeters??decimal.MaxValue);
                    async Task<decimal?> ConvertAltitude(decimal? altitude,decimal latitude,decimal longitude)
                    {
                        if(!altitude.HasValue||!bounded||m.AltitudeReference==zone.AltitudeReference)return altitude;
                        return await ConvertAltitudeAsync(altitude,latitude,longitude,m.AltitudeReference!,zone.AltitudeReference!,ct);
                    }
                    foreach(var point in orderedPoints.Where(x=>MissionRouteGeometry.Inside(new((double)x.Latitude,(double)x.Longitude),polygon)))
                    {
                        var converted=await ConvertAltitude(point.Altitude,point.Latitude,point.Longitude);
                        if(unknown||bounded&&!converted.HasValue) Finding(point.SequenceOrder,null,null,$"Cannot determine altitude clearance at waypoint {point.SequenceOrder} for '{zone.Name}': terrain, altitude, or reference data is unavailable.",true);
                        else if(Vertical(converted,converted)) Finding(point.SequenceOrder,null,null,$"Waypoint {point.SequenceOrder} violates airspace restriction '{zone.Name}'.",false);
                    }
                    for(int i=1;i<orderedPoints.Count;i++)
                    {
                        var first=orderedPoints[i-1];var last=orderedPoints[i];
                        var intervals=MissionRouteGeometry.InsideIntervals(new((double)first.Latitude,(double)first.Longitude),new((double)last.Latitude,(double)last.Longitude),polygon);
                        if(intervals.Count==0)continue;
                        var convertedIntervals=new List<(decimal? A,decimal? B)>();
                        foreach(var t in intervals)
                        {
                            decimal? rawA=first.Altitude+(last.Altitude-first.Altitude)*(decimal)t.Start;
                            decimal? rawB=first.Altitude+(last.Altitude-first.Altitude)*(decimal)t.End;
                            decimal latA=first.Latitude+(last.Latitude-first.Latitude)*(decimal)t.Start;
                            decimal lonA=first.Longitude+(last.Longitude-first.Longitude)*(decimal)t.Start;
                            decimal latB=first.Latitude+(last.Latitude-first.Latitude)*(decimal)t.End;
                            decimal lonB=first.Longitude+(last.Longitude-first.Longitude)*(decimal)t.End;
                            convertedIntervals.Add((await ConvertAltitude(rawA,latA,lonA),await ConvertAltitude(rawB,latB,lonB)));
                        }
                        if(unknown||bounded&&convertedIntervals.Any(x=>!x.A.HasValue||!x.B.HasValue)) Finding(null,first.SequenceOrder,last.SequenceOrder,$"Cannot determine altitude clearance for segment {first.SequenceOrder}-{last.SequenceOrder} at '{zone.Name}': terrain, altitude, or reference data is unavailable.",true);
                        else if(convertedIntervals.Any(x=>Vertical(Math.Min(x.A!.Value,x.B!.Value),Math.Max(x.A.Value,x.B.Value)))) Finding(null,first.SequenceOrder,last.SequenceOrder,$"Segment {first.SequenceOrder}-{last.SequenceOrder} violates airspace restriction '{zone.Name}'.",false);
                    }
                }
            }
            catch (JsonException) { r.Incomplete=true; r.Issues.Add($"Airspace restriction '{zone.Name}' could not be checked."); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { r.Error=true; r.Issues.Add($"Airspace restriction '{zone.Name}' failed validation due to an internal processing error."); }
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
                if (recent is not null && recent.PlanHash == Hash(m) && Decode(recent)?.RestrictionRevision == await CurrentRestrictionHashAsync(ct)) { await tx.CommitAsync(ct); return Decode(recent)!; }
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

    private static RestrictionEvidence Evidence(Geofence zone,string text)=>new()
    {
        GeofenceId=zone.Id,Revision=zone.Revision,Name=zone.Name,RestrictionType=zone.RestrictionType,
        CoordinatesJson=zone.Coordinates,VerticalRule=zone.VerticalRule,AltitudeReference=zone.AltitudeReference,
        MinAltitudeMeters=zone.MinAltitudeMeters,MaxAltitudeMeters=zone.MaxAltitudeMeters,Text=text
    };

    private static bool CoversRoute(IReadOnlyList<MissionRouteGeometry.Point> route,IReadOnlyList<MissionRouteGeometry.Point> polygon)
    {
        if(route.Count==0||route.Any(x=>!MissionRouteGeometry.Inside(x,polygon)))return false;
        for(var i=1;i<route.Count;i++)
        {
            var ranges=MissionRouteGeometry.InsideIntervals(route[i-1],route[i],polygon)
                .Where(x=>x.End-x.Start>1e-10).OrderBy(x=>x.Start).ToList();
            var covered=0d;
            foreach(var range in ranges){if(range.Start>covered+1e-10)return false;covered=Math.Max(covered,range.End);}
            if(covered<1-1e-10)return false;
        }
        return true;
    }

    private async Task<decimal?> ConvertAltitudeAsync(decimal? altitude,decimal latitude,decimal longitude,string missionReference,string restrictionReference,CancellationToken ct)
    {
        if(!altitude.HasValue||missionReference==restrictionReference)return altitude;
        var ground=await terrain.GetElevationAsync(latitude,longitude,ct);
        if(!ground.Available)return null;
        return missionReference=="AGL"&&restrictionReference=="MSL"?altitude+ground.ElevationMslMeters:
            missionReference=="MSL"&&restrictionReference=="AGL"?altitude-ground.ElevationMslMeters:null;
    }

}
