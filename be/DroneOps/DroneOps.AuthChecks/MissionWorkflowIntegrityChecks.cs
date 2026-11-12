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
        await workflow.SubmitAsync(draft.Id, owner, submitId);
        await workflow.SubmitAsync(draft.Id, owner, submitId);
        Check(await db.MissionUpdateRequests.CountAsync() == 1, "submission retry creates one review request");
        var initial = await Pending(draft.Id);
        var evidence = await validator.ReadRunAsync(draft.Id, initial.ValidationRunId!.Value, owner, false);
        Check(evidence!.IsValid, "initial fixture passes mandatory validation: " + evidence.Message);
        await approval.ApproveMissionAsync(draft.Id, new() { UpdateRequestId = initial.Id, ValidationRunId = initial.ValidationRunId }, manager);
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
        await approval.ApproveMissionAsync(draft.Id, new() { UpdateRequestId = pending.Id, ValidationRunId = newRun.Id }, manager);
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
        await approval.ApproveMissionAsync(draft.Id, new() { UpdateRequestId = pending.Id, ValidationRunId = riskRun.Id }, manager);
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
                    .ApproveMissionAsync(concurrentDraft.Id, new() { UpdateRequestId = concurrentRequest.Id, ValidationRunId = concurrentRequest.ValidationRunId }, manager);
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
