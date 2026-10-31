using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.Validators;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Services.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;


namespace DroneOps.Application.Validators.Missions;

public sealed class MissionValidationService(
    IMissionValidationRepository repository,
    IMissionWeatherService weather)
    : IMissionValidationService
{
    public static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    // =========================================================
    // Decode
    // =========================================================

    public static MissionValidationResult? Decode(
        MissionValidationRun? run)
    {
        return run is null
            ? null
            : JsonSerializer.Deserialize<MissionValidationResult>(
                run.ResultJson,
                JsonOptions);
    }

    // =========================================================
    // Restriction Hash
    // =========================================================

    public static string RestrictionHash(
        IEnumerable<Geofence> zones)
    {
        return HashRestrictionRevisions(
            zones.Select(x => (x.Id, x.Revision)));
    }

    private static string HashRestrictionRevisions(
        IEnumerable<(Guid Id, long Revision)> zones)
    {
        var data = zones
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.Revision
            });

        var json = JsonSerializer.Serialize(
            data,
            JsonOptions);

        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(json)));
    }

    private async Task<string> CurrentRestrictionHashAsync(
        CancellationToken cancellationToken)
    {
        var revisions =
            await repository.GetGeofenceRevisionsAsync(
                cancellationToken);

        return HashRestrictionRevisions(revisions);
    }

    // =========================================================
    // Latest Validation
    // =========================================================

    public async Task<MissionValidationRun?> LatestRunAsync(
        Guid missionId,
        CancellationToken cancellationToken = default)
    {
        return await repository.GetLatestRunAsync(
            missionId,
            cancellationToken);
    }

    public async Task<MissionValidationResult?> LatestAsync(
        Guid missionId,
        CancellationToken cancellationToken = default)
    {
        var run = await LatestRunAsync(
            missionId,
            cancellationToken);

        return Decode(run);
    }

    public async Task<MissionValidationResult?> LatestPreflightAsync(
        Guid missionId,
        CancellationToken cancellationToken = default)
    {
        var run =
            await repository.GetLatestPreflightRunAsync(
                missionId,
                cancellationToken);

        return Decode(run);
    }

    // =========================================================
    // Read Validation
    // =========================================================

    public async Task<MissionValidationResult?> ReadAsync(
        Guid missionId,
        Guid actor,
        bool manager,
        CancellationToken cancellationToken = default)
    {
        var mission =
            await repository.GetMissionWithWaypointsAsync(
                missionId,
                false,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                "Mission not found.");

        if (!manager && mission.CreatedBy != actor)
        {
            throw new UnauthorizedAccessException(
                "Mission is outside your scope.");
        }

        var run = await repository.GetLatestRunAsync(
            missionId,
            cancellationToken);

        var result = Decode(run);

        if (result is not null && run is not null)
        {
            result.Stale =
                run.PlanHash != Hash(mission) ||
                result.RestrictionRevision !=
                await CurrentRestrictionHashAsync(
                    cancellationToken);
        }

        return result;
    }

    public async Task<MissionValidationResult?> ReadLatestRunAsync(
        Guid missionId,
        Guid actor,
        bool manager,
        CancellationToken cancellationToken = default)
    {
        var mission =
            await repository.GetMissionAsync(
                missionId,
                false,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                "Mission not found.");

        if (!manager && mission.CreatedBy != actor)
        {
            throw new UnauthorizedAccessException(
                "Mission is outside your scope.");
        }

        return Decode(
            await repository.GetLatestRunAsync(
                missionId,
                cancellationToken));
    }

    public async Task<MissionValidationResult?> ReadRunAsync(
        Guid missionId,
        Guid runId,
        Guid actor,
        bool manager,
        CancellationToken cancellationToken = default)
    {
        var mission =
            await repository.GetMissionAsync(
                missionId,
                false,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                "Mission not found.");

        if (!manager && mission.CreatedBy != actor)
        {
            throw new UnauthorizedAccessException(
                "Mission is outside your scope.");
        }

        var run =
            await repository.GetRunAsync(
                missionId,
                runId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                "Validation result not found.");

        return Decode(run);
    }

    // =========================================================
    // Mission Hash
    // =========================================================

    private static string? CanonicalTime(DateTime? value)
    {
        return value.HasValue
            ? DateTime.SpecifyKind(
                    value.Value,
                    DateTimeKind.Utc)
                .ToString(
                    "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'",
                    System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }

    private static string? CanonicalNumber(decimal? value)
    {
        return value?.ToString(
            "G29",
            System.Globalization.CultureInfo.InvariantCulture);
    }

    public static object Snapshot(Mission mission)
    {
        return new
        {
            mission.Id,
            mission.CreatedBy,
            mission.UAVId,
            mission.Name,
            mission.Description,
            mission.AltitudeReference,

            StartTime = CanonicalTime(mission.StartTime),
            EndTime = CanonicalTime(mission.EndTime),

            Waypoints = mission.Waypoints
                .OrderBy(x => x.SequenceOrder)
                .Select(x => new
                {
                    x.SequenceOrder,
                    Latitude = CanonicalNumber(x.Latitude),
                    Longitude = CanonicalNumber(x.Longitude),
                    Altitude = CanonicalNumber(x.Altitude),
                    x.ActionType
                })
        };
    }

    public static string Hash(Mission mission)
    {
        var json = JsonSerializer.Serialize(
            Snapshot(mission),
            JsonOptions);

        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(json)));
    }

    // =========================================================
    // Store Validation
    // =========================================================

    public async Task StoreAsync(
        Mission mission,
        Guid actor,
        MissionValidationResult result,
        bool persisted = true,
        CancellationToken cancellationToken = default)
    {
        result.MissionId = persisted
            ? mission.Id
            : null;

        var run = new MissionValidationRun
        {
            Id = result.Id,
            MissionId = result.MissionId,
            ActorId = actor,
            Stage = result.Stage,
            IsValid = result.IsValid,
            CheckedAt = result.CheckedAt,

            PlanHash = Hash(mission),

            PlanJson = JsonSerializer.Serialize(
                Snapshot(mission),
                JsonOptions),

            ResultJson = JsonSerializer.Serialize(
                result,
                JsonOptions)
        };

        await repository.AddValidationRunAsync(
            run,
            cancellationToken);
    }

    // =========================================================
    // Evaluate
    // =========================================================

    public async Task<MissionValidationResult> EvaluateAsync(
        Mission mission,
        string stage,
        CancellationToken cancellationToken = default)
    {
        var result = new MissionValidationResult
        {
            MissionId = mission.Id,
            Stage = stage
        };

        // -----------------------------------------------------
        // Structural validation
        // -----------------------------------------------------

        var dto = new CreateMissionRequest
        {
            Name = mission.Name,
            Description = mission.Description,
            AltitudeReference = mission.AltitudeReference,
            UavId = mission.UAVId ?? Guid.Empty,
            StartTime = mission.StartTime ?? default,
            EndTime = mission.EndTime ?? default,

            Waypoints = mission.Waypoints
                .Select(x => new CreateWaypointRequest
                {
                    SequenceOrder = x.SequenceOrder,
                    Latitude = x.Latitude,
                    Longitude = x.Longitude,
                    Altitude = x.Altitude,
                    ActionType = x.ActionType
                })
                .ToList()
        };

        var structural =
            await new CreateMissionRequestValidator()
                .ValidateAsync(
                    dto,
                    cancellationToken);

        result.Issues.AddRange(
            structural.Errors
                .Select(x => x.ErrorMessage)
                .Distinct());

        // -----------------------------------------------------
        // Flight window validation
        // -----------------------------------------------------

        if (mission.StartTime is null ||
            mission.EndTime is null)
        {
            result.Issues.Add(
                "A complete flight window is required.");
        }

        if (result.Issues.Count > 0)
        {
            return result;
        }
        else
        {
            if ((stage is "Creation" or
                    "Update" or
                    "Manager" or
                    "Approval") &&
                mission.StartTime <= DateTime.UtcNow)
            {
                result.Issues.Add(
                    "Flight start time must be in the future.");
            }

            if (mission.EndTime <= DateTime.UtcNow)
            {
                result.Issues.Add(
                    "The planned flight window has ended.");
            }
        }
        if (result.Issues.Count > 0)
        {
            return result;
        }
        // -----------------------------------------------------
        // Pilot validation
        // -----------------------------------------------------

        var pilot =
            await repository.GetPilotByUserIdAsync(
                mission.CreatedBy,
                cancellationToken);

        if (pilot is null)
        {
            result.Issues.Add(
                "Operator pilot profile is missing.");
        }
        else
        {
            var currentDate =
                DateOnly.FromDateTime(
                    DateTime.UtcNow);

            var licenseInvalid =
                string.IsNullOrWhiteSpace(
                    pilot.PilotLicenseNo) ||
                pilot.LicenseIssuedDate is null ||
                pilot.LicenseExpiredDate is null ||
                pilot.LicenseIssuedDate > currentDate ||
                pilot.LicenseExpiredDate < currentDate;

            if (!licenseInvalid &&
                mission.StartTime is not null)
            {
                licenseInvalid =
                    pilot.LicenseIssuedDate >
                    DateOnly.FromDateTime(
                        mission.StartTime.Value);
            }

            if (!licenseInvalid &&
                mission.EndTime is not null)
            {
                licenseInvalid =
                    pilot.LicenseExpiredDate <
                    DateOnly.FromDateTime(
                        mission.EndTime.Value);
            }

            if (licenseInvalid)
            {
                result.Issues.Add(
                    "Pilot license must be valid throughout the scheduled flight.");
            }

        }

        if (result.Issues.Count > 0)
        {
            return result;
        }

        // -----------------------------------------------------
        // UAV validation
        // -----------------------------------------------------

        UAV? uav = null;

        if (mission.UAVId.HasValue)
        {
            uav = await repository.GetUavByIdAsync(
                mission.UAVId.Value,
                cancellationToken);
        }

        if (uav is null ||
            pilot is null ||
            uav.PilotId != pilot.Id)
        {
            result.Issues.Add(
                "The UAV must belong to the mission operator.");
        }

        if (uav is not null &&
            !string.Equals(
                uav.Status,
                "Active",
                StringComparison.OrdinalIgnoreCase))
        {
            result.Issues.Add(
                "The assigned UAV is not active.");
        }
        if (result.Issues.Count > 0)
        {
            return result;
        }


        // -----------------------------------------------------
        // Mission schedule conflict
        // -----------------------------------------------------

        var hasConflict =
            await repository.HasScheduleConflictAsync(
                mission.Id,
                mission.UAVId,
                mission.CreatedBy,
                mission.StartTime,
                mission.EndTime,
                cancellationToken);

        if (hasConflict)
        {
            result.Issues.Add(
                "The UAV or operator has an overlapping approved/active mission.");
        }
        if (result.Issues.Count > 0)
        {
            return result;
        }
        // -----------------------------------------------------
        // Route validation
        // -----------------------------------------------------

        var orderedPoints = mission.Waypoints
            .OrderBy(x => x.SequenceOrder)
            .ToList();

        if (orderedPoints
            .Zip(orderedPoints.Skip(1))
            .Any(x =>
                Math.Abs(
                    x.First.Longitude -
                    x.Second.Longitude) > 180))
        {
            result.Issues.Add(
                "Routes crossing the antimeridian require an unsupported geospatial model.");
        }
        if (result.Issues.Count > 0)
        {
            return result;
        }
        // -----------------------------------------------------
        // Geofence validation
        // -----------------------------------------------------

        var zones =
            await repository.GetGeofencesAsync(
                cancellationToken);

        result.RestrictionRevision =
            RestrictionHash(zones);

        var route = orderedPoints
            .Select(x =>
                new MissionRouteGeometry.Point(
                    (double)x.Latitude,
                    (double)x.Longitude))
            .ToList();

        if (zones.Count == 0)
        {
            result.Incomplete = true;

            result.Issues.Add(
                "No airspace restriction data is configured.");
        }

        foreach (var zone in zones)
        {
            try
            {
                var polygon =
                    JsonSerializer.Deserialize<
                        List<MissionRouteGeometry.Point>>(
                        zone.Coordinates,
                        JsonOptions);

                // =============================================
                // VALIDATE POLYGON DATA
                // =============================================

                if (polygon is null ||
                    polygon.Count < 3 ||
                    polygon.Any(point =>
                        point is null ||
                        !double.IsFinite(point.Latitude) ||
                        !double.IsFinite(point.Longitude) ||
                        Math.Abs(point.Latitude) > 90 ||
                        Math.Abs(point.Longitude) > 180))
                {
                    result.Incomplete = true;

                    result.Issues.Add(
                        $"Airspace restriction '{zone.Name}' " +
                        "has invalid coordinates and cannot be checked.");

                    continue;
                }

                // Route không giao với vùng cấm này thì bỏ qua.
                if (!MissionRouteGeometry.Intersects(
                        route,
                        polygon))
                {
                    continue;
                }

                // Polygon không hợp lệ nhưng đang giao với route.
                if (!MissionRouteGeometry.ValidPolygon(
                        polygon))
                {
                    result.Incomplete = true;

                    result.Issues.Add(
                        $"Airspace restriction '{zone.Name}' " +
                        "intersects the submitted route, " +
                        "but its geometry is invalid.");

                    continue;
                }

                // =============================================
                // CHECK WAYPOINT INSIDE RESTRICTED AREA
                // =============================================

                foreach (var point in orderedPoints.Where(
                             waypoint =>
                                 MissionRouteGeometry.Inside(
                                     new MissionRouteGeometry.Point(
                                         (double)waypoint.Latitude,
                                         (double)waypoint.Longitude),
                                     polygon)))
                {
                    var reason =
                        $"Waypoint {point.SequenceOrder} " +
                        $"violates restricted airspace '{zone.Name}'.";

                    result.Issues.Add(reason);

                    result.Findings.Add(
                        new RestrictionFinding
                        {
                            GeofenceId = zone.Id,
                            Revision = zone.Revision,

                            WaypointSequence =
                                point.SequenceOrder,

                            SegmentFrom = null,
                            SegmentTo = null,

                            Rule =
                                "RestrictedAirspaceIntersection",

                            Reason = reason
                        });
                }

                // =============================================
                // CHECK ROUTE SEGMENTS CROSSING RESTRICTED AREA
                // =============================================

                for (var index = 1;
                     index < orderedPoints.Count;
                     index++)
                {
                    var first =
                        orderedPoints[index - 1];

                    var last =
                        orderedPoints[index];

                    var intervals =
                        MissionRouteGeometry.InsideIntervals(
                            new MissionRouteGeometry.Point(
                                (double)first.Latitude,
                                (double)first.Longitude),

                            new MissionRouteGeometry.Point(
                                (double)last.Latitude,
                                (double)last.Longitude),

                            polygon);

                    if (intervals.Count == 0)
                    {
                        continue;
                    }

                    var reason =
                        $"Segment {first.SequenceOrder}-{last.SequenceOrder} " +
                        $"violates restricted airspace '{zone.Name}'.";

                    result.Issues.Add(reason);

                    result.Findings.Add(
                        new RestrictionFinding
                        {
                            GeofenceId = zone.Id,
                            Revision = zone.Revision,

                            WaypointSequence = null,

                            SegmentFrom =
                                first.SequenceOrder,

                            SegmentTo =
                                last.SequenceOrder,

                            Rule =
                                "RestrictedAirspaceIntersection",

                            Reason = reason
                        });
                }
            }
            catch (JsonException)
            {
                result.Incomplete = true;

                result.Issues.Add(
                    $"Airspace restriction '{zone.Name}' " +
                    "could not be checked.");
            }
        }
        if (result.Issues.Count > 0 ||
    result.Incomplete)
        {
            return result;
        }
        // -----------------------------------------------------
        // Weather validation
        // -----------------------------------------------------

        result.Weather =
            await weather.GetAsync(
                mission,
                cancellationToken);

        result.Warnings.AddRange(
            result.Weather.Warnings);

        return result;
    }

    // =========================================================
    // Revalidate
    // =========================================================

    public async Task<MissionValidationResult> RevalidateAsync(
        Guid missionId,
        Guid actor,
        bool manager,
        string stage,
        CancellationToken cancellationToken = default)
    {
        return await repository.ExecuteSerializableAsync(
            async ct =>
            {
                var mission =
                    await repository
                        .GetMissionWithWaypointsAsync(
                            missionId,
                            true,
                            ct)
                    ?? throw new KeyNotFoundException(
                        "Mission not found.");

                if (!manager &&
                    mission.CreatedBy != actor)
                {
                    throw new UnauthorizedAccessException(
                        "You may only validate your own mission.");
                }

                // ---------------------------------------------
                // Preflight
                // ---------------------------------------------

                if (stage == "Preflight")
                {
                    if (mission.Status != "Approved")
                    {
                        throw new InvalidOperationException(
                            "Preflight checks require an approved mission.");
                    }

                    var recent =
                        await repository
                            .GetRecentPreflightRunAsync(
                                missionId,
                                DateTimeOffset.UtcNow
                                    .AddMinutes(-1),
                                ct);

                    if (recent is not null &&
                        recent.PlanHash ==
                        Hash(mission))
                    {
                        var recentResult =
                            Decode(recent);

                        if (recentResult is not null)
                        {
                            var currentRestriction =
                                await CurrentRestrictionHashAsync(
                                    ct);

                            if (recentResult
                                    .RestrictionRevision ==
                                currentRestriction)
                            {
                                return recentResult;
                            }
                        }
                    }
                }

                // ---------------------------------------------
                // Run validation
                // ---------------------------------------------

                var result =
                    await EvaluateAsync(
                        mission,
                        stage,
                        ct);

                await StoreAsync(
                    mission,
                    actor,
                    result,
                    true,
                    ct);

                if (stage == "Preflight")
                {
                    await SyncPreflightAlertAsync(
                        missionId,
                        result,
                        ct);
                }

                return result;
            },
            cancellationToken);
    }

    // =========================================================
    // Preflight Incident
    // =========================================================

    public async Task SyncPreflightAlertAsync(
        Guid missionId,
        MissionValidationResult result,
        CancellationToken cancellationToken = default)
    {
        var existing =
            await repository
                .GetOpenPreflightIncidentAsync(
                    missionId,
                    cancellationToken);

        // Need an alert but none exists
        if ((!result.IsValid ||
             result.Warnings.Count > 0) &&
            existing is null)
        {
            var incident = new Incident
            {
                Id = Guid.NewGuid(),
                MissionId = missionId,

                Title = result.IsValid
                    ? "Preflight weather advisory"
                    : "Preflight blocked",

                Description =
                    result.Message +
                    " " +
                    string.Join(
                        " ",
                        result.Warnings),

                Type = "PreflightValidation",

                Severity = result.IsValid
                    ? "Medium"
                    : "High",

                Status = "Open",

                CreatedAt =
                    DateTimeOffset.UtcNow,

                Evidence =
                    JsonSerializer.Serialize(
                        result,
                        JsonOptions)
            };

            await repository.AddIncidentAsync(
                incident,
                cancellationToken);

            return;
        }

        if (existing is null)
        {
            return;
        }

        // Validation is now clean
        if (result.IsValid &&
            result.Warnings.Count == 0)
        {
            existing.Status = "Resolved";

            existing.ResolvedAt =
                DateTimeOffset.UtcNow;

            existing.ResolutionNotes =
                "A later preflight check passed without warnings.";

            return;
        }

        // Update existing alert
        existing.Title = result.IsValid
            ? "Preflight weather advisory"
            : "Preflight blocked";

        existing.Severity = result.IsValid
            ? "Medium"
            : "High";

        existing.Description =
            result.Message +
            " " +
            string.Join(
                " ",
                result.Warnings);

        existing.Evidence =
            JsonSerializer.Serialize(
                result,
                JsonOptions);
    }
}