using DroneOps.Application.Services.UAVs;
using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

internal static class UavReviewChecks
{
    public static async Task<int> RunAsync(bool postgres = false)
    {
        var passed = 0;
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); passed++; Console.WriteLine($"PASS: {name}"); }
        async Task Reject(Func<Task> action, string name) { try { await action(); } catch (InvalidOperationException) { Check(true, name); return; } throw new InvalidOperationException(name); }
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        if (postgres) builder.UseNpgsql("Host=127.0.0.1;Port=55965;Database=kan65_isolated;Username=kan65", options => options.EnableRetryOnFailure());
        else builder.UseInMemoryDatabase($"review-{Guid.NewGuid()}");
        var options = builder.Options;
        await using var db = new AppDbContext(options);
        if (postgres)
        {
            await db.Database.EnsureCreatedAsync();
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "database/migrations/20261114000100_uav_review_proposals.sql"))) root = root.Parent;
            if (root is null) throw new InvalidOperationException("Migration source not found.");
            await db.Database.ExecuteSqlRawAsync(File.ReadAllText(Path.Combine(root.FullName, "database/migrations/20261114000100_uav_review_proposals.sql")));
        }
        var owner = Guid.NewGuid(); var manager = Guid.NewGuid(); var id = Guid.NewGuid(); var pilotId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = roleId, Name = "uav_operator" });
        db.Users.Add(new User { RoleId = roleId, Id = owner, Email = "review@fixture.test", FullName = "Fixture", IsActive = true });
        db.Pilots.Add(new Pilot { Id = pilotId, UserId = owner, PilotLicenseNo = "Fixture", LicenseIssuedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)), LicenseExpiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) });
        db.UAVs.Add(new UAV { Id = id, PilotId = pilotId, Code = "REVIEW-01", Name = "Approved name", Status = "Active", BatteryCapacityMah = 5000 });
        await db.SaveChangesAsync();
        var workflow = new UavReviewWorkflow(db);
        var proposed = UavReviewWorkflow.Snapshot(await db.UAVs.AsNoTracking().SingleAsync());
        proposed.ExpectedVersion = 0; proposed.Name = "Proposed name"; proposed.BatteryCapacityMah = 7000;
        try { await workflow.ReadAsync(id, Guid.NewGuid(), false, default); throw new Exception("Missing scope guard"); } catch (UnauthorizedAccessException) { Check(true, "another Operator cannot read UAV proposals or history"); }
        try { await workflow.StageAsync(id, Guid.NewGuid(), proposed, default); throw new Exception("Missing ownership guard"); } catch (UnauthorizedAccessException) { Check(true, "another Operator cannot submit UAV changes"); }
        await workflow.StageAsync(id, owner, proposed, default);
        var current = await db.UAVs.AsNoTracking().SingleAsync();
        var pending = await db.UavReviewRequests.AsNoTracking().SingleAsync();
        Check(current.Name == "Approved name" && current.BatteryCapacityMah == 5000 && current.Status == "Active" && current.HasPendingProposal, "proposal preserves approved specifications and operational status");
        Check(pending.SubmittedBy == owner && pending.BaseInformationJson.Contains("5000") && pending.ProposedInformationJson.Contains("7000"), "proposal captures immutable before/after evidence and actor");
        await Reject(() => workflow.StageAsync(id, owner, proposed, default), "stale edit version cannot overwrite pending proposal");
        await Reject(() => workflow.DecideAsync(id, manager, "approve", new() { ExpectedVersion = 0, RequestId = pending.Id, Reason = "Approve fixture" }, default), "stale Manager version rejected");
        await Reject(() => workflow.DecideAsync(id, manager, "approve", new() { ExpectedVersion = 1, RequestId = Guid.NewGuid(), Reason = "Approve fixture" }, default), "wrong proposal identity rejected");
        await workflow.DecideAsync(id, manager, "reject", new() { ExpectedVersion = 1, RequestId = pending.Id, Reason = "Incorrect battery specification" }, default);
        current = await db.UAVs.AsNoTracking().SingleAsync();
        Check(current.Name == "Approved name" && current.BatteryCapacityMah == 5000 && current.Status == "Active" && !current.HasPendingProposal, "rejecting update retains approved information and Active status");
        var decision = await db.UavReviewRequests.AsNoTracking().SingleAsync();
        Check(decision.ReviewedBy == manager && decision.ReviewedAt != null && decision.Reason == "Incorrect battery specification", "decision persists reviewer/time/reason");
        proposed.ExpectedVersion = current.RegistryVersion;
        await workflow.StageAsync(id, owner, proposed, default);
        current = await db.UAVs.AsNoTracking().SingleAsync(); pending = await db.UavReviewRequests.AsNoTracking().SingleAsync(x => x.Status == "Pending");
        await workflow.DecideAsync(id, manager, "approve", new() { ExpectedVersion = current.RegistryVersion, RequestId = pending.Id, Reason = "Verified specifications" }, default);
        current = await db.UAVs.AsNoTracking().SingleAsync();
        Check(current.Name == "Proposed name" && current.BatteryCapacityMah == 7000 && current.HasApprovedInformation && current.Status == "Active", "approval applies only the selected proposal and retains ownership");
        Check(current.PilotId == pilotId && await db.UavReviewRequests.CountAsync() == 2, "resubmission preserves earlier rejected audit and UAV owner");
        var revision = current.RegistryVersion;
        await workflow.DecideAsync(id, manager, "deactivate", new() { ExpectedVersion = revision, Reason = "Maintenance" }, default);
        Check((await db.UAVs.AsNoTracking().SingleAsync()).Status == "Inactive", "deactivation is audited separately from information proposals");
        current = await db.UAVs.AsNoTracking().SingleAsync();
        await workflow.DecideAsync(id, manager, "activate", new() { ExpectedVersion = current.RegistryVersion, Reason = "Maintenance completed" }, default);
        var flightId = Guid.NewGuid();
        db.Missions.Add(new Mission { Id = flightId, CreatedBy = owner, UAVId = id, Name = "Active fixture", Status = "InProgress" });
        db.MissionFlightExecutions.Add(new MissionFlightExecution { MissionId = flightId, UavId = id, StartedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
        current = await db.UAVs.AsNoTracking().SingleAsync();
        await Reject(() => workflow.DecideAsync(id, manager, "deactivate", new() { ExpectedVersion = current.RegistryVersion, Reason = "Should block" }, default), "active execution blocks deactivation");
        proposed.ExpectedVersion = current.RegistryVersion; proposed.Name = "Unsafe midflight update";
        await Reject(() => workflow.StageAsync(id, owner, proposed, default), "active execution blocks information changes");
        db.MissionFlightExecutions.RemoveRange(db.MissionFlightExecutions); db.Missions.RemoveRange(db.Missions); await db.SaveChangesAsync();
        db.DroneHardwareIdentities.Add(new DroneHardwareIdentity { UavId = id, Identity = "fixture-binding" }); await db.SaveChangesAsync();
        proposed.Code = "OTHER-FC";
        await Reject(() => workflow.StageAsync(id, owner, proposed, default), "bound FC identity cannot be replaced by registry edits");
        var mission = new Mission { Id = Guid.NewGuid(), CreatedBy = owner, UAVId = id, StartTime = DateTime.UtcNow.AddHours(1), EndTime = DateTime.UtcNow.AddHours(2) };
        Check(await FlightEligibility.BlockingReasonAsync(db, mission, default) == null, "eligible active Operator/UAV accepted for planned window");
        var pilot = await db.Pilots.SingleAsync(); pilot.LicenseExpiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)); await db.SaveChangesAsync();
        Check(await FlightEligibility.BlockingReasonAsync(db, mission, default) != null, "expired credentials block mission approval/start eligibility");
        if (postgres)
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var eligibilityTransaction = await db.Database.BeginTransactionAsync();
                await FlightEligibility.LockAircraftAsync(db, id, default);
                await eligibilityTransaction.CommitAsync();
            });
            Check(true, "PostgreSQL eligibility acquires UAV/User/Pilot locks in account-workflow order");
            // Both requests carry the same observed version; exactly one may commit.
            db.ChangeTracker.Clear();
            current = await db.UAVs.AsNoTracking().SingleAsync();
            var version = current.RegistryVersion;
            async Task<bool> Race()
            {
                await using var contender = new AppDbContext(options);
                try { await new UavReviewWorkflow(contender).DecideAsync(id, manager, "deactivate", new() { ExpectedVersion = version, Reason = "Concurrent fixture" }, default); return true; }
                catch (InvalidOperationException) { return false; }
            }
            var results = await Task.WhenAll(Race(), Race());
            Check(results.Count(x => x) == 1, "PostgreSQL concurrent decisions commit once with stale loser rejected");
        }
        var legacyId = Guid.NewGuid();
        db.ChangeTracker.Clear();
        db.UAVs.Add(new UAV { Id = legacyId, PilotId = pilotId, Code = "LEGACY-REG", Name = "Legacy registration", Status = "Pending" });
        await db.SaveChangesAsync();
        await Reject(() => workflow.DecideAsync(legacyId, manager, "approve", new() { Reason = "Missing version" }, default), "Manager decision requires an explicit observed version");
        await workflow.DecideAsync(legacyId, manager, "reject", new() { ExpectedVersion = 0, Reason = "Provide evidence" }, default);
        var legacy = await db.UAVs.AsNoTracking().SingleAsync(x => x.Id == legacyId);
        var legacyAudit = await db.UavReviewRequests.AsNoTracking().SingleAsync(x => x.UavId == legacyId);
        Check(legacy.Status == "Rejected" && legacyAudit.SubmittedBy == null && legacyAudit.SubmittedAt == null, "legacy decision does not fabricate old submission identity/time");
        var corrected = UavReviewWorkflow.Snapshot(legacy); corrected.ExpectedVersion = legacy.RegistryVersion;
        await workflow.StageAsync(legacyId, owner, corrected, default);
        legacy = await db.UAVs.AsNoTracking().SingleAsync(x => x.Id == legacyId);
        var correctedRequest = await db.UavReviewRequests.AsNoTracking().SingleAsync(x => x.UavId == legacyId && x.Status == "Pending");
        Check(legacy.Status == "Pending", "rejected registration can resubmit unchanged specifications after evidence is corrected");
        await Reject(() => workflow.DecideAsync(legacyId, owner, "approve", new() { ExpectedVersion = legacy.RegistryVersion, RequestId = correctedRequest.Id, Reason = "Self approval" }, default), "Manager cannot approve own submission");
        await workflow.DecideAsync(legacyId, manager, "approve", new() { ExpectedVersion = legacy.RegistryVersion, RequestId = correctedRequest.Id, Reason = "Evidence verified" }, default);
        Check((await db.UAVs.AsNoTracking().SingleAsync(x => x.Id == legacyId)).Status == "Active", "corrected registration becomes Active only after Manager approval");
        db.ChangeTracker.Clear();
        legacy = await db.UAVs.AsNoTracking().SingleAsync(x => x.Id == legacyId);
        var telemetryMission = Guid.NewGuid();
        db.Missions.Add(new Mission { Id = telemetryMission, CreatedBy = owner, UAVId = legacyId, Name = "Armed evidence fixture", Status = "Approved" });
        var sample = new TelemetryRecord { Id = Guid.NewGuid(), UAVId = legacyId, MissionId = telemetryMission,
            IsValid = true, Armed = true, SourceTimestamp = DateTimeOffset.UtcNow, ReceivedAt = DateTimeOffset.UtcNow };
        db.TelemetryRecords.Add(sample); await db.SaveChangesAsync();
        corrected = UavReviewWorkflow.Snapshot(legacy); corrected.ExpectedVersion = legacy.RegistryVersion; corrected.Name = "Postflight correction";
        await Reject(() => workflow.StageAsync(legacyId, owner, corrected, default), "fresh armed FC evidence blocks specification changes even before an execution is recorded");
        sample = await db.TelemetryRecords.SingleAsync(x => x.Id == sample.Id);
        sample.SourceTimestamp = DateTimeOffset.UtcNow.AddMinutes(-5); sample.ReceivedAt = sample.SourceTimestamp;
        await db.SaveChangesAsync();
        await workflow.StageAsync(legacyId, owner, corrected, default);
        Check(await db.UavReviewRequests.AnyAsync(x => x.UavId == legacyId && x.Status == "Pending"), "stale armed sample is not represented as a current armed observation");
        await using var firstImageWriter = new AppDbContext(options);
        await using var staleImageWriter = new AppDbContext(options);
        var firstImage = await firstImageWriter.UAVs.SingleAsync(x => x.Id == legacyId);
        var staleImage = await staleImageWriter.UAVs.SingleAsync(x => x.Id == legacyId);
        firstImage.ImageStoragePath = "fixture-first.png"; await firstImageWriter.SaveChangesAsync();
        staleImage.ImageStoragePath = "fixture-stale.png";
        try { await staleImageWriter.SaveChangesAsync(); throw new Exception("Missing image concurrency guard"); }
        catch (DbUpdateConcurrencyException) { Check(true, "concurrent optional-image changes cannot overwrite a newer UAV row"); }
        return passed;
    }
}
