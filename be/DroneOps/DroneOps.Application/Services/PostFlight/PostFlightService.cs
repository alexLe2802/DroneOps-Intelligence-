using System.Collections.Concurrent;
using DroneOps.Application.DTOs.Request.PostFlight;
using DroneOps.Application.DTOs.Response.Common;
using DroneOps.Application.DTOs.Response.PostFlight;
using DroneOps.Application.Interfaces.PostFlight;
using DroneOps.Application.Settings;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Interfaces;
using Microsoft.Extensions.Options;

namespace DroneOps.Application.Services.PostFlight;

public sealed class PostFlightService : IPostFlightService
{
    private static readonly string[] FinalizableStatuses =
        ["In Progress", "InProgress", "Active", "Flying", "Executing"];
    private static readonly string[] TrustedCompletionEventTypes =
        ["MISSION_COMPLETED", "FLIGHT_COMPLETED", "LANDED"];
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> MissionLocks = new();
    private readonly IPostFlightRepository _repository;
    private readonly int _communicationLossSeconds;

    public PostFlightService(IPostFlightRepository repository)
        : this(repository, Microsoft.Extensions.Options.Options.Create(new TelemetryMonitoringOptions())) { }

    public PostFlightService(
        IPostFlightRepository repository,
        IOptions<TelemetryMonitoringOptions> monitoringOptions)
    {
        _repository = repository;
        _communicationLossSeconds = Math.Max(1, monitoringOptions.Value.CommunicationLossSeconds);
    }

    public async Task<int> SynchronizeOverdueMissionsAsync(
        CancellationToken cancellationToken = default)
    {
        var synchronized = await _repository.SynchronizeOverdueMissionsAsync(
            DateTimeOffset.UtcNow, cancellationToken);
        if (synchronized.CompletedMissions > 0)
            await ProcessQueuedReportsAsync(100, cancellationToken);
        return synchronized.CompletedMissions + synchronized.CancelledMissions;
    }

    public async Task<PagedResponse<FlightHistoryItemResponse>> GetFlightHistoryAsync(
        GetFlightHistoryRequest request,
        Guid? pilotUserId = null,
        CancellationToken cancellationToken = default)
    {
        await SynchronizeOverdueMissionsAsync(cancellationToken);
        ValidateDateRange(request.FromDate, request.ToDate);
        var inclusiveToDate = NormalizeInclusiveToDate(request.ToDate);
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var (missions, totalCount) = await _repository.GetFlightHistoryAsync(
            pageNumber, pageSize, request.Search, request.Status, request.UavId,
            request.FromDate, inclusiveToDate, pilotUserId, cancellationToken);

        var items = new List<FlightHistoryItemResponse>(missions.Count);
        foreach (var mission in missions)
        {
            var report = await _repository.GetReportByMissionIdAsync(mission.Id, cancellationToken);
            var incidents = await _repository.GetIncidentsByMissionIdAsync(mission.Id, cancellationToken);
            items.Add(new FlightHistoryItemResponse
            {
                MissionId = mission.Id,
                MissionName = mission.Name,
                Status = mission.Status,
                UavId = mission.UAVId,
                UavName = mission.UAV?.Name ?? "N/A",
                UavCode = mission.UAV?.Code ?? "N/A",
                PilotName = mission.Creator?.FullName ?? "N/A",
                StartTime = mission.StartTime,
                EndTime = mission.EndTime,
                DurationMinutes = report?.TotalFlightDurationMinutes ?? CalculateScheduledDuration(mission),
                TotalIncidents = incidents.Count,
                HasReport = report?.GenerationStatus is "Available" or "Outdated",
                ReportStatus = report?.GenerationStatus ?? "NotQueued",
                ReportRevision = report?.Revision,
                TelemetryCoverageComplete = report?.GenerationStatus == "Available"
            });
        }

        return new PagedResponse<FlightHistoryItemResponse>
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalItems = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<PostFlightReportResponse> RecordTrustedCompletionAsync(
        string idempotencyKey,
        RecordFlightCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("Idempotency-Key is required.");
        if (request.MissionId == Guid.Empty || request.UavId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.SourceId))
            throw new ArgumentException("Mission, UAV and source identity are required.");
        if (!TrustedCompletionEventTypes.Contains(request.EventType, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Unsupported trusted completion event type.");
        if (request.CompletedAt == default || request.CompletedAt > DateTimeOffset.UtcNow.AddMinutes(5))
            throw new ArgumentException("Completion timestamp is invalid.");

        return await WithMissionLockAsync(request.MissionId, async () =>
        {
            var mission = await _repository.GetMissionWithDetailsAsync(request.MissionId, cancellationToken)
                ?? throw new KeyNotFoundException("Flight mission not found.");

            var completionEvent = new FlightCompletionEvent
            {
                Id = Guid.NewGuid(),
                MissionId = mission.Id,
                UAVId = request.UavId,
                SourceId = request.SourceId.Trim(),
                IdempotencyKey = idempotencyKey.Trim(),
                EventType = request.EventType.Trim().ToUpperInvariant(),
                CompletedAt = request.CompletedAt.ToUniversalTime(),
                ReceivedAt = DateTimeOffset.UtcNow,
                IsTrusted = true,
                Evidence = request.Evidence
            };
            var report = new PostFlightReport
            {
                Id = Guid.NewGuid(),
                MissionId = mission.Id,
                Revision = 1,
                IsCurrent = true,
                GenerationStatus = "Queued",
                Status = "Completed",
                CoveredTelemetryFrom = mission.StartTime.HasValue
                    ? new DateTimeOffset(DateTime.SpecifyKind(mission.StartTime.Value, DateTimeKind.Utc)) : null,
                CoveredTelemetryThrough = completionEvent.CompletedAt,
                CreatedAt = DateTimeOffset.UtcNow,
                GenerationReason = "Trusted completion event"
            };

            var claim = await _repository.ClaimCompletionAsync(
                completionEvent, report, FinalizableStatuses, cancellationToken);
            switch (claim.Outcome)
            {
                case "MissingMission": throw new KeyNotFoundException("Flight mission not found.");
                case "WrongUav": throw new InvalidOperationException("Completion event UAV does not match the mission assignment.");
                case "InvalidStatus": throw new InvalidOperationException($"Mission with status '{mission.Status}' cannot be completed; a trusted start is required.");
                case "BeforeStart": throw new InvalidOperationException("Completion timestamp cannot be earlier than the flight start.");
                case "Duplicate":
                {
                    var duplicate = await _repository.GetCompletionEventByKeyAsync(idempotencyKey.Trim(), cancellationToken)
                        ?? throw new InvalidOperationException("Completion replay could not be reloaded.");
                    if (!MatchesCompletion(duplicate, request))
                        throw new InvalidOperationException("Idempotency-Key was already used for a different completion event.");
                    break;
                }
                case "AlreadyCompleted":
                {
                    var existing = await _repository.GetTrustedCompletionEventAsync(request.MissionId, cancellationToken)
                        ?? throw new InvalidOperationException("Mission is completed without trusted completion evidence.");
                    if (!MatchesCompletion(existing, request))
                        throw new InvalidOperationException("An ambiguous completion conflicts with the trusted event already recorded for this flight.");
                    break;
                }
                case "Created": break;
                default: throw new InvalidOperationException("Trusted completion could not be recorded.");
            }

            var currentMission = await _repository.GetMissionWithDetailsAsync(request.MissionId, cancellationToken)
                ?? throw new KeyNotFoundException("Flight mission not found.");
            var currentReport = await _repository.GetReportByMissionIdAsync(request.MissionId, cancellationToken)
                ?? throw new InvalidOperationException("Completion was recorded but its report queue entry is missing.");
            return MapToReportResponse(currentReport, currentMission);
        });
    }

    private static bool MatchesCompletion(
        FlightCompletionEvent existing,
        RecordFlightCompletionRequest request) =>
        existing.MissionId == request.MissionId && existing.UAVId == request.UavId &&
        string.Equals(existing.SourceId, request.SourceId.Trim(), StringComparison.Ordinal) &&
        string.Equals(existing.EventType, request.EventType.Trim(), StringComparison.OrdinalIgnoreCase) &&
        existing.CompletedAt == request.CompletedAt.ToUniversalTime();

    public async Task<PostFlightReportResponse> FinalizeAndGenerateReportAsync(
        Guid missionId,
        FinalizeFlightRequest? request = null,
        Guid? currentUserId = null,
        bool isManager = false,
        CancellationToken cancellationToken = default)
    {
        if (!isManager)
            throw new UnauthorizedAccessException("Only an Operations Manager can manually process a post-flight report.");
        var mission = await GetAuthorizedMissionAsync(missionId, currentUserId, isManager, cancellationToken);
        var completion = await _repository.GetTrustedCompletionEventAsync(missionId, cancellationToken)
            ?? throw new InvalidOperationException("A trusted completion event is required before report generation.");
        var report = await _repository.GetReportByMissionIdAsync(missionId, cancellationToken);
        if (report == null)
        {
            report = new PostFlightReport
            {
                Id = Guid.NewGuid(), MissionId = missionId, Revision = 1, IsCurrent = true,
                GenerationStatus = "Queued", Status = "Completed", CreatedAt = DateTimeOffset.UtcNow,
                CoveredTelemetryFrom = mission.StartTime.HasValue
                    ? new DateTimeOffset(DateTime.SpecifyKind(mission.StartTime.Value, DateTimeKind.Utc)) : null,
                CoveredTelemetryThrough = completion.CompletedAt, RequestedBy = currentUserId,
                Notes = request?.Notes, Summary = request?.Summary, GenerationReason = "Manager processing request"
            };
            await _repository.AddReportAsync(report, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        await GenerateReportAsync(report, mission, cancellationToken);
        return MapToReportResponse(report, mission);
    }

    public async Task<int> ProcessQueuedReportsAsync(
        int batchSize = 10,
        CancellationToken cancellationToken = default)
    {
        await _repository.MarkReportsOutdatedForLateTelemetryAsync(cancellationToken);
        var reports = await _repository.GetReportsAwaitingGenerationAsync(Math.Clamp(batchSize, 1, 100), cancellationToken);
        var processed = 0;
        foreach (var report in reports)
        {
            await WithMissionLockAsync(report.MissionId, async () =>
            {
                await GenerateReportAsync(report, report.Mission, cancellationToken);
                return true;
            });
            processed++;
        }
        return processed;
    }

    public async Task<PostFlightReportResponse> RegenerateReportAsync(
        Guid missionId,
        RegeneratePostFlightReportRequest request,
        Guid managerUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            throw new ArgumentException("A regeneration reason of up to 500 characters is required.");

        return await WithMissionLockAsync(missionId, async () =>
        {
            var mission = await _repository.GetMissionWithDetailsAsync(missionId, cancellationToken)
                ?? throw new KeyNotFoundException("Flight mission not found.");
            var completion = await _repository.GetTrustedCompletionEventAsync(missionId, cancellationToken)
                ?? throw new InvalidOperationException("A trusted completion event is required before regeneration.");
            var revisions = await _repository.GetReportRevisionsAsync(missionId, cancellationToken);
            var current = await _repository.GetReportByMissionIdAsync(missionId, cancellationToken)
                ?? throw new KeyNotFoundException("Post-flight report not found.");
            var next = new PostFlightReport
            {
                Id = Guid.NewGuid(), MissionId = missionId,
                Revision = revisions.Count == 0 ? 1 : revisions.Max(x => x.Revision) + 1,
                IsCurrent = true, GenerationStatus = "Queued", Status = current.Status,
                CoveredTelemetryFrom = current.CoveredTelemetryFrom,
                CoveredTelemetryThrough = completion.CompletedAt, CreatedAt = DateTimeOffset.UtcNow,
                RequestedBy = managerUserId, GenerationReason = request.Reason.Trim(), Notes = current.Notes
            };
            await _repository.ReplaceCurrentReportAsync(current, next, cancellationToken);
            return MapToReportResponse(next, mission);
        });
    }

    private async Task GenerateReportAsync(
        PostFlightReport report,
        Mission mission,
        CancellationToken cancellationToken)
    {
        if (report.GenerationStatus != "Queued" &&
            !(report.GenerationStatus == "Generating" &&
              report.GenerationStartedAt < DateTimeOffset.UtcNow.AddMinutes(-5))) return;
        report.GenerationStatus = "Generating";
        report.GenerationStartedAt = DateTimeOffset.UtcNow;
        report.FailureReason = null;
        await _repository.SaveChangesAsync(cancellationToken);

        try
        {
            var completion = await _repository.GetTrustedCompletionEventAsync(mission.Id, cancellationToken);
            var scheduledCompletion = report.GenerationReason == "Scheduled mission end time elapsed";
            if (completion == null && !scheduledCompletion)
                throw new InvalidOperationException("Trusted completion evidence is missing.");
            var coveredThrough = completion?.CompletedAt ?? report.CoveredTelemetryThrough
                ?? throw new InvalidOperationException("The completed flight window is missing.");
            var telemetry = (await _repository.GetTelemetryByMissionIdAsync(mission.Id, cancellationToken))
                .Where(x => (!report.CoveredTelemetryFrom.HasValue || x.SourceTimestamp >= report.CoveredTelemetryFrom) &&
                            x.SourceTimestamp <= coveredThrough)
                .ToList();
            var incidents = await _repository.GetIncidentsByMissionIdAsync(mission.Id, cancellationToken);
            if (telemetry.Count == 0 && !scheduledCompletion)
                throw new InvalidOperationException("No valid telemetry is available for the completed flight window.");
            if (telemetry.Count == 0)
                PopulateScheduledReport(report, mission, incidents);
            else
                PopulateReport(report, mission, telemetry, incidents);
            report.CoveredTelemetryThrough = coveredThrough;
            report.GenerationStatus = "Available";
            report.GeneratedAt = DateTimeOffset.UtcNow;
            report.FailedAt = null;
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            report.GenerationStatus = "Failed";
            report.FailedAt = DateTimeOffset.UtcNow;
            report.FailureReason = exception.Message.Length <= 500 ? exception.Message : exception.Message[..500];
            await _repository.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<PostFlightReportResponse> GetReportByMissionIdAsync(
        Guid missionId, Guid? currentUserId, bool isManager,
        CancellationToken cancellationToken = default)
    {
        var mission = await GetAuthorizedMissionAsync(missionId, currentUserId, isManager, cancellationToken);
        var report = await _repository.GetReportByMissionIdAsync(missionId, cancellationToken)
            ?? throw new KeyNotFoundException("Post-flight report has not been queued.");
        return MapToReportResponse(report, mission);
    }

    public async Task<List<PostFlightReportResponse>> GetReportRevisionsAsync(
        Guid missionId, Guid? currentUserId, bool isManager,
        CancellationToken cancellationToken = default)
    {
        var mission = await GetAuthorizedMissionAsync(missionId, currentUserId, isManager, cancellationToken);
        var reports = await _repository.GetReportRevisionsAsync(missionId, cancellationToken);
        return reports.Select(report => MapToReportResponse(report, mission)).ToList();
    }

    public async Task<List<FlightTelemetryPointResponse>> GetTelemetryPlaybackAsync(
        Guid missionId, Guid? currentUserId, bool isManager,
        CancellationToken cancellationToken = default)
    {
        await GetAuthorizedMissionAsync(missionId, currentUserId, isManager, cancellationToken);
        var records = await _repository.GetTelemetryByMissionIdAsync(missionId, cancellationToken);
        return records.Select(record => new FlightTelemetryPointResponse
        {
            Latitude = record.Latitude, Longitude = record.Longitude, Altitude = record.Altitude,
            Speed = record.Speed, BatteryLevel = record.BatteryLevel, RecordedAt = record.SourceTimestamp
        }).ToList();
    }

    public async Task<List<Incident>> GetIncidentsAsync(
        Guid missionId, Guid? currentUserId, bool isManager,
        CancellationToken cancellationToken = default)
    {
        await GetAuthorizedMissionAsync(missionId, currentUserId, isManager, cancellationToken);
        return await _repository.GetIncidentsByMissionIdAsync(missionId, cancellationToken);
    }

    public async Task<OperationalAnalyticsResponse> GetOperationalAnalyticsAsync(
        GetOperationalAnalyticsRequest request,
        Guid? pilotUserId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateDateRange(request.FromDate, request.ToDate);
        var inclusiveToDate = NormalizeInclusiveToDate(request.ToDate);
        var missions = await _repository.GetStartedMissionsAsync(
            request.FromDate, inclusiveToDate, request.UavId, request.MissionId,
            pilotUserId, cancellationToken);
        var reports = new List<PostFlightReport>();
        var incidents = new List<Incident>();
        decimal recordedDistanceMeters = 0;
        var validDistanceSegments = 0;
        var excludedDistanceSegments = 0;
        double excludedGapSeconds = 0;
        var missingGpsSamples = 0;
        var invalidTelemetrySamples = 0;
        var missionsWithoutGpsCoverage = 0;
        foreach (var mission in missions)
        {
            var report = await _repository.GetReportByMissionIdAsync(mission.Id, cancellationToken);
            if (report != null) reports.Add(report);
            incidents.AddRange(await _repository.GetIncidentsByMissionIdAsync(mission.Id, cancellationToken));
            var distance = CalculateRecordedDistance(
                await _repository.GetTelemetryForAnalyticsAsync(mission.Id, cancellationToken),
                _communicationLossSeconds);
            recordedDistanceMeters += distance.Meters;
            validDistanceSegments += distance.ValidSegments;
            excludedDistanceSegments += distance.ExcludedSegments;
            excludedGapSeconds += distance.ExcludedGapSeconds;
            missingGpsSamples += distance.MissingGpsSamples;
            invalidTelemetrySamples += distance.InvalidTelemetrySamples;
            if (distance.ValidSegments == 0) missionsWithoutGpsCoverage++;
        }

        var eligibleReports = reports.Where(x => x.GenerationStatus == "Available").ToList();
        var completed = missions.Count(x => x.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
        var distinctIncidents = incidents.DistinctBy(x => x.Id).ToList();
        var exclusions = missions
            .Select(mission => reports.FirstOrDefault(report => report.MissionId == mission.Id)?.GenerationStatus ?? "NoReport")
            .Where(status => status != "Available")
            .GroupBy(status => status)
            .ToDictionary(group => group.Key, group => group.Count());

        return new OperationalAnalyticsResponse
        {
            TotalFlights = missions.Count,
            CompletedFlights = completed,
            AbortedOrFailedFlights = missions.Count - completed,
            TotalFlightHours = Math.Round(eligibleReports.Sum(x => x.TotalFlightDurationMinutes) / 60.0, 2),
            TotalDistanceKilometers = Math.Round(recordedDistanceMeters / 1000m, 2),
            TotalIncidentsRecorded = distinctIncidents.Count,
            SuccessRatePercentage = missions.Count == 0 ? null : Math.Round(completed * 100.0 / missions.Count, 1),
            CompletionRateStatus = missions.Count == 0 ? "Unavailable" : "Available",
            ReportsAvailable = eligibleReports.Count,
            ReportsExcluded = missions.Count - eligibleReports.Count,
            ReportCoveragePercentage = missions.Count == 0 ? null : Math.Round(eligibleReports.Count * 100.0 / missions.Count, 1),
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            UavId = request.UavId,
            MissionId = request.MissionId,
            CompletionTrend = missions
                .GroupBy(x => DateOnly.FromDateTime(x.StartTime!.Value))
                .OrderBy(x => x.Key)
                .Select(group => new AnalyticsTrendPointResponse
                {
                    Date = group.Key,
                    TotalFlights = group.Count(),
                    CompletedFlights = group.Count(x => x.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase)),
                    FailedOrAbortedFlights = group.Count(x => !x.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                }).ToList(),
            IncidentSeverityBreakdown = distinctIncidents
                .GroupBy(x => NormalizeSeverity(x.Severity))
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            ExcludedReportReasons = exclusions,
            Contract = new AnalyticsContractResponse
            {
                CommunicationLossThresholdSeconds = _communicationLossSeconds
            },
            MissingCoverage = new AnalyticsCoverageResponse
            {
                ValidDistanceSegments = validDistanceSegments,
                ExcludedDistanceSegments = excludedDistanceSegments,
                ExcludedGapSeconds = Math.Round(excludedGapSeconds, 3),
                MissingGpsSamples = missingGpsSamples,
                InvalidTelemetrySamples = invalidTelemetrySamples,
                MissionsWithoutValidGpsCoverage = missionsWithoutGpsCoverage,
                MissionsWithoutAvailableReport = missions.Count - eligibleReports.Count
            }
        };
    }

    private static RecordedDistance CalculateRecordedDistance(
        IReadOnlyList<TelemetryRecord> telemetry,
        int communicationLossSeconds)
    {
        decimal meters = 0;
        var validSegments = 0;
        var excludedSegments = 0;
        double excludedGapSeconds = 0;
        var missingGpsSamples = 0;
        var invalidTelemetrySamples = 0;
        TelemetryRecord? previous = null;
        foreach (var point in telemetry)
        {
            if (!point.IsValid)
            {
                invalidTelemetrySamples++;
                previous = null;
                continue;
            }
            if (!point.Latitude.HasValue || !point.Longitude.HasValue ||
                point.Latitude is < -90 or > 90 || point.Longitude is < -180 or > 180)
            {
                missingGpsSamples++;
                previous = null;
                continue;
            }
            if (previous != null)
            {
                var gap = (point.SourceTimestamp - previous.SourceTimestamp).TotalSeconds;
                if (gap >= 0 && gap <= communicationLossSeconds)
                {
                    meters += CalculateDistanceMeters(previous.Latitude!.Value, previous.Longitude!.Value,
                        point.Latitude.Value, point.Longitude.Value);
                    validSegments++;
                }
                else
                {
                    excludedSegments++;
                    if (gap > 0) excludedGapSeconds += gap;
                }
            }
            previous = point;
        }
        return new(Math.Round(meters, 2), validSegments, excludedSegments, excludedGapSeconds,
            missingGpsSamples, invalidTelemetrySamples);
    }

    private sealed record RecordedDistance(
        decimal Meters, int ValidSegments, int ExcludedSegments, double ExcludedGapSeconds,
        int MissingGpsSamples, int InvalidTelemetrySamples);

    private async Task<Mission> GetAuthorizedMissionAsync(
        Guid missionId, Guid? currentUserId, bool isManager, CancellationToken cancellationToken)
    {
        var mission = await _repository.GetMissionWithDetailsAsync(missionId, cancellationToken)
            ?? throw new KeyNotFoundException("Flight mission not found.");
        if (!isManager && (!currentUserId.HasValue || mission.CreatedBy != currentUserId.Value))
            throw new UnauthorizedAccessException("You do not have permission to access this mission history.");
        return mission;
    }

    private async Task<T> WithMissionLockAsync<T>(Guid missionId, Func<Task<T>> action)
    {
        var gate = MissionLocks.GetOrAdd(missionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try { return await action(); }
        finally { gate.Release(); }
    }

    private static void ValidateDateRange(DateTime? fromDate, DateTime? toDate)
    {
        if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
            throw new ArgumentException("FromDate must be earlier than or equal to ToDate.");
    }

    private static DateTime? NormalizeInclusiveToDate(DateTime? toDate) =>
        toDate.HasValue && toDate.Value.TimeOfDay == TimeSpan.Zero
            ? toDate.Value.Date.AddDays(1).AddTicks(-1)
            : toDate;

    private static string NormalizeSeverity(string? severity)
    {
        if (string.IsNullOrWhiteSpace(severity)) return "Unknown";
        var value = severity.Trim().ToLowerInvariant();
        return value switch
        {
            "critical" => "Critical",
            "high" => "High",
            "medium" => "Medium",
            "low" => "Low",
            _ => "Unknown"
        };
    }

    private static void PopulateReport(
        PostFlightReport report,
        Mission mission,
        IReadOnlyList<TelemetryRecord> telemetry,
        IReadOnlyList<Incident> incidents)
    {
        var firstAt = telemetry[0].SourceTimestamp;
        var lastAt = telemetry[^1].SourceTimestamp;
        report.TotalFlightDurationMinutes = Math.Round(Math.Max(0, (lastAt - firstAt).TotalMinutes), 2);
        var altitudes = telemetry.Where(x => x.Altitude.HasValue).Select(x => x.Altitude!.Value).ToList();
        report.MaxAltitude = altitudes.Count == 0 ? null : altitudes.Max();
        report.AvgAltitude = altitudes.Count == 0 ? null : Math.Round(altitudes.Average(), 2);
        var speeds = telemetry.Where(x => x.Speed.HasValue).Select(x => x.Speed!.Value).ToList();
        report.MaxSpeed = speeds.Count == 0 ? null : speeds.Max();
        report.AvgSpeed = speeds.Count == 0 ? null : Math.Round(speeds.Average(), 2);
        var batteries = telemetry.Where(x => x.BatteryLevel.HasValue).Select(x => x.BatteryLevel!.Value).ToList();
        report.InitialBatteryLevel = batteries.Count == 0 ? null : batteries[0];
        report.FinalBatteryLevel = batteries.Count == 0 ? null : batteries[^1];
        report.BatteryConsumedPercentage = report.InitialBatteryLevel.HasValue && report.FinalBatteryLevel.HasValue
            ? Math.Max(0, report.InitialBatteryLevel.Value - report.FinalBatteryLevel.Value) : null;

        decimal distance = 0;
        TelemetryRecord? previous = null;
        foreach (var point in telemetry.Where(x => x.Latitude.HasValue && x.Longitude.HasValue))
        {
            if (previous != null)
                distance += CalculateDistanceMeters(previous.Latitude!.Value, previous.Longitude!.Value,
                    point.Latitude!.Value, point.Longitude!.Value);
            previous = point;
        }
        report.TotalDistanceMeters = Math.Round(distance, 2);
        report.TotalTelemetryRecords = telemetry.Count;
        report.TotalIncidents = incidents.Count;
        report.SafetyEvaluation = BuildSafetyEvaluation(incidents);
        report.Summary ??= $"Flight '{mission.Name}' completed with {telemetry.Count} valid telemetry records and {incidents.Count} incident(s).";
        report.Status = "Completed";
    }

    private static void PopulateScheduledReport(
        PostFlightReport report,
        Mission mission,
        IReadOnlyList<Incident> incidents)
    {
        report.TotalFlightDurationMinutes = CalculateScheduledDuration(mission);
        report.TotalTelemetryRecords = 0;
        report.TotalIncidents = incidents.Count;
        report.Status = "Completed";
        report.SafetyEvaluation = BuildSafetyEvaluation(incidents);
        report.Summary ??= incidents.Count == 0
            ? $"Flight '{mission.Name}' completed safely at its scheduled end time; no incidents were recorded."
            : $"Flight '{mission.Name}' completed at its scheduled end time with {incidents.Count} recorded incident(s).";
    }

    private static string BuildSafetyEvaluation(IReadOnlyList<Incident> incidents)
    {
        if (incidents.Count == 0) return "LOW RISK: No incidents were recorded during flight.";
        if (incidents.Any(x => x.Severity is "Critical" or "High"))
            return "HIGH RISK: Critical or high-severity incidents were detected during flight.";
        return "MEDIUM RISK: Minor or medium-severity incidents were recorded during flight.";
    }

    private static decimal CalculateDistanceMeters(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
    {
        const double radius = 6371000;
        static double ToRadians(decimal degrees) => (double)degrees * Math.PI / 180;
        var latitude1 = ToRadians(lat1);
        var latitude2 = ToRadians(lat2);
        var deltaLatitude = ToRadians(lat2 - lat1);
        var deltaLongitude = ToRadians(lon2 - lon1);
        var a = Math.Sin(deltaLatitude / 2) * Math.Sin(deltaLatitude / 2) +
                Math.Cos(latitude1) * Math.Cos(latitude2) *
                Math.Sin(deltaLongitude / 2) * Math.Sin(deltaLongitude / 2);
        return (decimal)(radius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)));
    }

    private static double CalculateScheduledDuration(Mission mission) =>
        mission.StartTime.HasValue && mission.EndTime.HasValue
            ? Math.Round(Math.Max(0, (mission.EndTime.Value - mission.StartTime.Value).TotalMinutes), 2)
            : 0;

    private static PostFlightReportResponse MapToReportResponse(PostFlightReport report, Mission mission) => new()
    {
        ReportId = report.Id,
        MissionId = mission.Id,
        MissionName = mission.Name,
        MissionDescription = mission.Description,
        Status = report.Status,
        GenerationStatus = report.GenerationStatus,
        Revision = report.Revision,
        IsCurrent = report.IsCurrent,
        CoveredTelemetryFrom = report.CoveredTelemetryFrom,
        CoveredTelemetryThrough = report.CoveredTelemetryThrough,
        GeneratedAt = report.GeneratedAt,
        FailureReason = report.FailureReason,
        GenerationReason = report.GenerationReason,
        UavName = mission.UAV?.Name ?? "N/A",
        UavCode = mission.UAV?.Code ?? "N/A",
        PilotName = mission.Creator?.FullName ?? "N/A",
        StartTime = mission.StartTime,
        EndTime = mission.EndTime,
        TotalFlightDurationMinutes = report.TotalFlightDurationMinutes,
        MaxAltitude = report.MaxAltitude,
        AvgAltitude = report.AvgAltitude,
        MaxSpeed = report.MaxSpeed,
        AvgSpeed = report.AvgSpeed,
        InitialBatteryLevel = report.InitialBatteryLevel,
        FinalBatteryLevel = report.FinalBatteryLevel,
        BatteryConsumedPercentage = report.BatteryConsumedPercentage,
        TotalDistanceMeters = report.TotalDistanceMeters,
        TotalTelemetryRecords = report.TotalTelemetryRecords,
        TotalIncidents = report.TotalIncidents,
        SafetyEvaluation = report.SafetyEvaluation,
        Summary = report.Summary,
        Notes = report.Notes,
        CreatedAt = report.CreatedAt
    };
}
