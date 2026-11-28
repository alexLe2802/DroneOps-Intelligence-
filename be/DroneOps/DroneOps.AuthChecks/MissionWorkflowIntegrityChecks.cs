using DroneOps.API.Controllers.Missions;
using DroneOps.Persistence.Repositories.Notifications;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using DroneOps.API.Controllers;
using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Services.Missions;
using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;

internal static class MissionWorkflowIntegrityChecks
{
    private sealed class ReviewProvider : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var review = """{"riskLevel":"MEDIUM","advice":"Check evidence","recommendation":"Review weather","risks":[]}""";
            var body = new { candidates = new[] { new { content = new { parts = new[] { new { text = review } } } } } };
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        }
    }

    private sealed class Weather : IMissionWeatherService
    {
        public bool High;
        public Task<WeatherAdvisory> GetAsync(Mission mission, CancellationToken ct = default) => Task.FromResult(new WeatherAdvisory
        {
            Available = true, OverallRisk = High ? "High" : "Low", Status = "Available",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10), MaxWindMetersPerSecond = High ? 18 : 1
        });
    }
    public static async Task RunAsync(bool postgres = false)
    {
        // Tests real services/repositories; InMemory does not prove PostgreSQL isolation or constraints.
        var options = new DbContextOptionsBuilder<AppDbContext>();
        if (postgres)
        {
            var connection = Environment.GetEnvironmentVariable("DRONEOPS_CI_POSTGRES") is not null
                ? CiDatabaseChecks.LocalConnection()
                : new Npgsql.NpgsqlConnectionStringBuilder($"Host=/tmp/droneops-mf1-pg;Port=55491;Username={Environment.UserName}");
            connection.Database = $"droneops_ci_workflow_{Guid.NewGuid():N}";
            options.UseNpgsql(connection.ConnectionString, o => o.EnableRetryOnFailure());
        }
        else options.UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        await using var db = new AppDbContext(options.Options);
        if (postgres)
        {
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Geofence\" ALTER COLUMN \"Revision\" SET DEFAULT 1");
        }
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var weather = new Weather();
        var validator = new MissionValidationService(new MissionValidationRepository(db, cache), weather);
        var workflow = new MissionWorkflowService(new MissionWorkflowRepository(db), validator, db);
        var approval = new MissionApprovalService(new MissionApprovalRepository(db), validator, db);
        var owner = Guid.NewGuid(); var manager = Guid.NewGuid(); var otherManager = Guid.NewGuid(); var pilot = Guid.NewGuid(); var uav = Guid.NewGuid();
        var operatorRole = Guid.NewGuid(); var managerRole = Guid.NewGuid();
        db.Roles.AddRange(new Role { Id = operatorRole, Name = "uav_operator" }, new Role { Id = managerRole, Name = "operation_manager" });
        db.Users.AddRange(new User { Id = manager, RoleId = managerRole, FullName = "Manager", Email = "manager@mf1.test" },
            new User { Id = otherManager, RoleId = managerRole, FullName = "Other Manager", Email = "other@mf1.test" });
        db.Users.Add(new User { Id = owner, RoleId = operatorRole, FullName = "Workflow pilot", Email = "mf1@example.test", IsActive = true });
        db.Pilots.Add(new Pilot { Id = pilot, UserId = owner, PilotLicenseNo = "MF1", LicenseIssuedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)), LicenseExpiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) });
        db.UAVs.Add(new UAV { Id = uav, PilotId = pilot, Name = "Fixture UAV", Code = "MF1", Status = "Active", MaxFlightTimeMinutes = 60 });
        db.Geofences.Add(new Geofence { Id = Guid.NewGuid(), Name = "Distant prohibited area", VerticalRule = "AllAltitudes", Revision = 1,
            Coordinates = "[{\"latitude\":20,\"longitude\":110},{\"latitude\":21,\"longitude\":110},{\"latitude\":21,\"longitude\":111},{\"latitude\":20,\"longitude\":111}]" });
        await db.SaveChangesAsync();
        var count = 0;
        void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); count++; Console.WriteLine("PASS: " + message); }
        async Task Conflict(Func<Task> call, string message)
        {
            try { await call(); throw new Exception("FAIL: " + message); }
            catch (InvalidOperationException) { Check(true, message); }
        }
        CreateMissionRequest Plan(string name) => new()
        {
            OperationId = Guid.NewGuid(), UavId = uav, Name = name, AltitudeReference = "MSL",
            StartTime = DateTime.UtcNow.AddDays(2), EndTime = DateTime.UtcNow.AddDays(2).AddMinutes(20),
            AverageSpeedMetersPerSecond = 5, MaxSpeedMetersPerSecond = 8,
            Waypoints = [new() { SequenceOrder = 1, Latitude = 10, Longitude = 106, Altitude = 50, ActionType = "TakeOff" },
                         new() { SequenceOrder = 2, Latitude = 10.001m, Longitude = 106.001m, Altitude = 50, ActionType = "Land" }]
        };
        async Task<Mission> Read(Guid id) => await db.Missions.AsNoTracking().Include(m => m.Waypoints).SingleAsync(m => m.Id == id);
        async Task<MissionUpdateRequest> Pending(Guid id) => await db.MissionUpdateRequests.AsNoTracking().SingleAsync(r => r.MissionId == id && r.Status == "Pending");
        var plan = Plan("Initial");
        var draft = await workflow.SavePlanAsync(owner, plan, null);
        Check(draft.Status == "Draft" && !await db.MissionUpdateRequests.AnyAsync(), "creation saves Draft without submitting");
        Check((await workflow.SavePlanAsync(owner, plan, null)).Id == draft.Id && await db.Missions.CountAsync() == 1, "lost create response reuses the Draft identity");
        try { await workflow.SavePlanAsync(Guid.NewGuid(), plan, draft.Id); throw new Exception("Foreign operator edit accepted"); }
        catch (UnauthorizedAccessException) { Check(true, "another operator cannot edit this mission"); }
        var submitId = Guid.NewGuid();
        var submitted = await workflow.SubmitAsync(draft.Id, owner, submitId);
        var replayed = await workflow.SubmitAsync(draft.Id, owner, submitId);
        Check(submitted && !replayed, "only the first submission reports a new review transition");
        Check(await db.MissionUpdateRequests.CountAsync() == 1, "submission retry creates one review request");
        var initial = await Pending(draft.Id);
        var evidence = await validator.ReadRunAsync(draft.Id, initial.ValidationRunId!.Value, owner, false);
        Check(evidence!.IsValid, "initial fixture passes mandatory validation: " + evidence.Message);
        var provider = new ReviewProvider();
        using var http = new HttpClient(provider) { BaseAddress = new Uri("https://example.invalid/") };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Ai:ApiKey"] = "fixture", ["Ai:Model"] = "fixture" }).Build();
        var ai = new DroneOps.Application.Services.AIMissionReviewService(new GenericRepository<Mission>(db),
            new GenericRepository<MissionUpdateRequest>(db), validator, http, config, cache);
        await ai.ReviewMissionAsync(draft.Id, manager);
        var repeatedReview = await ai.ReviewMissionAsync(draft.Id, manager);
        Check(repeatedReview.MissionId == draft.Id && provider.Calls == 1, "reopening unchanged evidence reuses the AI review");
        await approval.ApproveMissionAsync(draft.Id, new() { UpdateRequestId = initial.Id, ValidationRunId = initial.ValidationRunId,
            PermitDecision = "Supplement", Comment = "Attach the permit for this site." }, manager);
        Check((await Read(draft.Id)).Status == "Awaiting Permit" && await db.Notifications.AnyAsync(n => n.RecipientUserId == owner && n.ReferenceId == draft.Id),
            "plan approval without a permit requests a supplement from its pilot");
        await Conflict(() => ai.ReviewMissionAsync(draft.Id, manager), "cached AI review still requires a pending mission");
        RegistryDocumentsController Documents(Guid actor, string role) => new(db, new MissionWorkflowRepository(db), new NotificationRepository(db))
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, actor.ToString()), new Claim(ClaimTypes.Role, role)], "fixture")) } }
        };
        var pilotDocuments = Documents(owner, "uav_operator");
        var managerDocuments = Documents(manager, "operation_manager");
        var pdf = new DroneOps.Application.DTOs.Request.RegistryFile("permit.pdf", Convert.ToBase64String("%PDF-fixture"u8.ToArray()));
        Check(await Documents(Guid.NewGuid(), "uav_operator").Supplement(draft.Id, pdf, default) is NotFoundResult, "another pilot cannot supplement a mission");
        await pilotDocuments.Supplement(draft.Id, pdf, default);
        var firstPermit = await db.RegistryDocuments.SingleAsync(x => x.MissionId == draft.Id);
        Check(await Documents(Guid.NewGuid(), "uav_operator").Download(firstPermit.Id, default) is NotFoundResult, "another pilot cannot download a private permit");
        await managerDocuments.Review(draft.Id, new(firstPermit.Id, false, "Wrong area"), default);
        Check((await Read(draft.Id)).Status == "Awaiting Permit", "rejecting only the permit reopens supplementation");
        await pilotDocuments.Supplement(draft.Id, pdf, default);
        var permitId = await db.RegistryDocuments.Where(x => x.MissionId == draft.Id && x.Decision == null).Select(x => x.Id).SingleAsync();
        Check(await managerDocuments.Review(draft.Id, new(firstPermit.Id, true, null), default) is ConflictObjectResult, "stale review cannot accept the replaced file");
        await managerDocuments.Review(draft.Id, new(permitId, true, null), default);
        Check((await Read(draft.Id)).Status == "Approved" && await db.MissionApprovals.CountAsync() == 1, "permit acceptance advances the existing approved plan");
        var original = await Read(draft.Id); var originalHash = MissionValidationService.Hash(original);
        var edit = MissionPlans.Dto(original); edit.Name = "Changed"; edit.OperationId = Guid.NewGuid();
        edit.ExpectedPlanHash = originalHash;
        await workflow.SavePlanAsync(owner, edit, draft.Id);
        Check((await Read(draft.Id)).Status == "Approved" && MissionValidationService.Hash(await Read(draft.Id)) == originalHash, "saving an update preserves Approved content and status");
        var pending = await Pending(draft.Id);
        Check((await approval.GetPendingApprovalsAsync(new(), manager)).Items.Any(x => x.UpdateRequestId == pending.Id && x.Status == "Approved"), "queue includes proposals for Approved missions");
        await approval.RejectMissionAsync(draft.Id, new() { UpdateRequestId = pending.Id, Reason = "Keep original" }, manager);
        Check((await Read(draft.Id)).Status == "Approved" && MissionValidationService.Hash(await Read(draft.Id)) == originalHash, "reject update preserves current Approved plan");
        Check((await approval.RejectMissionAsync(draft.Id, new() { UpdateRequestId = pending.Id, Reason = "Keep original" }, manager)).IdempotentReplay, "decision retry is bound to the exact request");
        edit.OperationId = Guid.NewGuid();
        await workflow.SavePlanAsync(owner, edit, draft.Id);
        pending = await Pending(draft.Id);
        Check(await db.MissionApprovals.CountAsync() == 2, "new proposal is not mistaken for a replay of initial approval");
        var oldPending = pending.Id;
        edit.OperationId = Guid.NewGuid(); edit.ExpectedPendingRequestId = oldPending; edit.Name = "Replacement";
        await workflow.SavePlanAsync(owner, edit, draft.Id);
        await Conflict(() => approval.ApproveMissionAsync(draft.Id, new() { UpdateRequestId = oldPending }, manager), "superseded request cannot be approved");
        var stale = MissionPlans.Dto(original); stale.OperationId = Guid.NewGuid(); stale.ExpectedPlanHash = originalHash;
        await Conflict(() => workflow.SavePlanAsync(owner, stale, draft.Id), "stale editor cannot silently supersede a newer request");
        pending = await Pending(draft.Id);
        var newRun = await validator.RevalidateAsync(draft.Id, owner, false, "Manager", default, pending.Id);
        Check(newRun.UpdateRequestId == pending.Id && newRun.IsValid, "manual revalidation targets the saved proposal");
        await Conflict(() => approval.ApproveMissionAsync(draft.Id, new() { UpdateRequestId = pending.Id, ValidationRunId = pending.ValidationRunId }, manager), "approval rejects a replaced validation identity");
        await approval.ApproveMissionAsync(draft.Id, new() { PermitDecision = "Valid", PermitDocumentId = permitId, UpdateRequestId = pending.Id, ValidationRunId = newRun.Id }, manager);
        Check((await Read(draft.Id)).Name == "Replacement", "approve applies the proposed plan to the same mission");
        var current = await Read(draft.Id);
        var managerEdit = MissionPlans.Dto(current); managerEdit.Name = "Manager proposal"; managerEdit.OperationId = Guid.NewGuid();
        await workflow.SavePlanAsync(manager, managerEdit, draft.Id, default, true);
        pending = await Pending(draft.Id);
        await Conflict(() => approval.ApproveMissionAsync(draft.Id, new() { UpdateRequestId = pending.Id, ValidationRunId = pending.ValidationRunId }, manager), "manager cannot approve their own proposed update");
        await approval.RejectMissionAsync(draft.Id, new() { UpdateRequestId = pending.Id, Reason = "Retain plan" }, otherManager);
        weather.High = true;
        var risk = MissionPlans.Dto(await Read(draft.Id)); risk.Name = "Risk proposal"; risk.OperationId = Guid.NewGuid();
        await workflow.SavePlanAsync(owner, risk, draft.Id);
        pending = await Pending(draft.Id);
        await Conflict(() => approval.ApproveMissionAsync(draft.Id, new() { UpdateRequestId = pending.Id, ValidationRunId = pending.ValidationRunId }, manager), "High Risk blocks approval before pilot consent");
        await workflow.ConfirmHighRiskAsync(draft.Id, owner, default, pending.Id, pending.ValidationRunId);
        Check((await validator.HistoryAsync(draft.Id, owner, false)).First(x => x.Id == pending.Id).PilotConfirmedBy == owner, "pilot confirmation retains actor and evidence identity");
        var riskRun = await validator.RevalidateAsync(draft.Id, owner, false, "Manager", default, pending.Id);
        Check((await validator.HistoryAsync(draft.Id, owner, false)).First(x => x.Id == pending.Id).RequiresPilotConfirmation, "revalidation invalidates old risk consent");
        await Conflict(() => workflow.ConfirmHighRiskAsync(draft.Id, owner, default, pending.Id, pending.ValidationRunId), "stale risk dialog cannot confirm replaced evidence");
        await workflow.ConfirmHighRiskAsync(draft.Id, owner, default, pending.Id, riskRun.Id);
        await approval.ApproveMissionAsync(draft.Id, new() { PermitDecision = "Valid", PermitDocumentId = permitId, UpdateRequestId = pending.Id, ValidationRunId = riskRun.Id }, manager);
        Check((await Read(draft.Id)).Name == "Risk proposal", "confirmed unchanged High Risk evidence can be approved");
        risk.OperationId = Guid.NewGuid(); risk.Name = "Declined risk";
        await workflow.SavePlanAsync(owner, risk, draft.Id);
        pending = await Pending(draft.Id);
        await workflow.CancelHighRiskAsync(draft.Id, owner, default, pending.Id);
        Check((await Read(draft.Id)).Status == "Approved" && (await Read(draft.Id)).Name == "Risk proposal", "declining risk withdraws proposal without deleting or downgrading current mission");
        weather.High = false;
        risk.OperationId = Guid.NewGuid(); risk.Name = "Invalid clearance"; risk.AltitudeReference = "AGL";
        (await db.UAVs.SingleAsync(x => x.Id == uav)).Status = "Inactive"; await db.SaveChangesAsync();
        await workflow.SavePlanAsync(owner, risk, draft.Id);
        pending = await Pending(draft.Id);
        Check((await validator.ReadRunAsync(draft.Id, pending.ValidationRunId!.Value, owner, false))!.IsValid == false, "failed or incomplete clearance remains persisted in review");
        await Conflict(() => approval.ApproveMissionAsync(draft.Id, new() { UpdateRequestId = pending.Id, ValidationRunId = pending.ValidationRunId }, manager), "failed mandatory validation cannot approve");
        await workflow.CancelAsync(draft.Id, owner, false, "End fixture");
        Check(!await db.MissionUpdateRequests.AnyAsync(r => r.MissionId == draft.Id && r.Status == "Pending"), "cancel closes every pending request with retained reason");
        await Conflict(() => approval.ApproveMissionAsync(draft.Id, new() { UpdateRequestId = pending.Id, ValidationRunId = pending.ValidationRunId }, manager), "cancelled mission cannot receive a later approval");
        await Conflict(() => workflow.StartAsync(draft.Id, owner, false), "browser cannot fabricate an InProgress flight");
        Check(!(new MissionValidationResult { Error = true }).IsValid, "Error evidence can never report IsValid");
        (await db.UAVs.SingleAsync(x => x.Id == uav)).Status = "Active";
        await db.SaveChangesAsync();
        foreach (var outcome in new[] { "Valid", "Exempt" })
        {
            var directPlan = Plan($"Direct approval: {outcome}");
            directPlan.FlightPermit = outcome == "Valid" ? pdf : null;
            var directDraft = await workflow.SavePlanAsync(owner, directPlan, null);
            var attachment = await db.RegistryDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.MissionId == directDraft.Id);
            Check(outcome == "Exempt" ? attachment == null : attachment?.Name == pdf.Name,
                $"draft persists the expected attachment for {outcome}");
            await workflow.SubmitAsync(directDraft.Id, owner, Guid.NewGuid());
            var request = await Pending(directDraft.Id);
            var result = await approval.ApproveMissionAsync(directDraft.Id, new()
            {
                UpdateRequestId = request.Id, ValidationRunId = request.ValidationRunId,
                PermitDocumentId = attachment?.Id, PermitDecision = outcome, Comment = "Reviewed fixture basis"
            }, manager);
            Check(result.NewStatus == "Approved" && await db.MissionApprovals.AnyAsync(x =>
                x.MissionId == directDraft.Id && x.PermitDecision == outcome && x.PermitDocumentId == (attachment == null ? null : attachment.Id)),
                $"{outcome} permit decision advances the plan directly with an audit record");
            await workflow.CancelAsync(directDraft.Id, owner, false, "Release fixture allocation");
        }
        if (postgres)
        {
            using var migrationStream = typeof(AppDbContext).Assembly.GetManifestResourceStream("DroneOps.Persistence.MissionReviewIntegrity.sql")!;
            using var migrationReader = new StreamReader(migrationStream);
            var migrationSql = await migrationReader.ReadToEndAsync();
            await db.Database.ExecuteSqlRawAsync(migrationSql);
            await db.Database.ExecuteSqlRawAsync(migrationSql);
            Check(true, "MF1 migration can be applied twice without rewriting history");
            (await db.UAVs.SingleAsync(x => x.Id == uav)).Status = "Active"; await db.SaveChangesAsync();
            var concurrentPlan = Plan("Concurrent review");
            concurrentPlan.StartTime = DateTime.UtcNow.AddDays(4); concurrentPlan.EndTime = concurrentPlan.StartTime.AddMinutes(20);
            var concurrentDraft = await workflow.SavePlanAsync(owner, concurrentPlan, null);
            await workflow.SubmitAsync(concurrentDraft.Id, owner, Guid.NewGuid());
            var concurrentRequest = await Pending(concurrentDraft.Id);
            async Task<MissionDecisionResponse> DecideInSeparateConnection()
            {
                await using var context = new AppDbContext(options.Options);
                using var localCache = new MemoryCache(new MemoryCacheOptions());
                var localValidation = new MissionValidationService(new MissionValidationRepository(context, localCache), weather);
                return await new MissionApprovalService(new MissionApprovalRepository(context), localValidation, context)
                    .ApproveMissionAsync(concurrentDraft.Id, new() { PermitDecision = "Exempt", Comment = "Fixture exemption basis", UpdateRequestId = concurrentRequest.Id, ValidationRunId = concurrentRequest.ValidationRunId }, manager);
            }
            var decisions = await Task.WhenAll(DecideInSeparateConnection(), DecideInSeparateConnection());
            Check(decisions.Count(x => x.IdempotentReplay) == 1 && await db.MissionApprovals.CountAsync(x => x.UpdateRequestId == concurrentRequest.Id) == 1,
                "concurrent PostgreSQL approvals persist one decision and replay the other");
            var baseline = await Read(concurrentDraft.Id);
            async Task<bool> CompetingSave(string name)
            {
                await using var context = new AppDbContext(options.Options);
                using var localCache = new MemoryCache(new MemoryCacheOptions());
                var localValidation = new MissionValidationService(new MissionValidationRepository(context, localCache), weather);
                var localWorkflow = new MissionWorkflowService(new MissionWorkflowRepository(context), localValidation, context);
                var payload = MissionPlans.Dto(baseline); payload.Name = name; payload.OperationId = Guid.NewGuid();
                payload.ExpectedPlanHash = MissionValidationService.Hash(baseline);
                try { await localWorkflow.SavePlanAsync(owner, payload, baseline.Id); return true; }
                catch (InvalidOperationException) { return false; }
            }
            var saved = await Task.WhenAll(CompetingSave("Concurrent A"), CompetingSave("Concurrent B"));
            Check(saved.Count(x => x) == 1 && await db.MissionUpdateRequests.CountAsync(x => x.MissionId == baseline.Id && x.Status == "Pending") == 1,
                "concurrent PostgreSQL saves leave one pending request and one stale conflict");
            Check((await Read(baseline.Id)).Status == "Approved" && MissionValidationService.Hash(await Read(baseline.Id)) == MissionValidationService.Hash(baseline),
                "concurrent proposals preserve approved content and its booking");
        }
        Console.WriteLine($"MF1 service/repository checks passed: {count}. Provider: {(postgres ? "PostgreSQL disposable fixture" : "InMemory")}. Browser acceptance requires separate execution.");
    }
}
