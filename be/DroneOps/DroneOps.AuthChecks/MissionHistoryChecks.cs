using System.Text.Json;
using DroneOps.API.Controllers.Missions;
using DroneOps.Application.DTOs.Request.Missions;
using DroneOps.Application.DTOs.Response.Missions;
using DroneOps.Application.Interfaces.Missions;
using DroneOps.Application.Validators.Missions;
using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Repositories;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

internal static class MissionHistoryChecks
{
    private sealed class NoWeather : IMissionWeatherService
    {
        public Task<WeatherAdvisory> GetAsync(Mission mission, CancellationToken ct = default) => throw new InvalidOperationException("History must not fetch new weather.");
    }
    public static async Task RunAsync()
    {
        var count = 0;
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); count++; Console.WriteLine("PASS: " + text); }
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new MissionValidationService(new MissionValidationRepository(db, cache), new NoWeather());
        var owner = Guid.NewGuid(); var manager = Guid.NewGuid();
        var mission = new Mission { Id = Guid.NewGuid(), CreatedBy = owner, Name = "History fixture" };
        db.Missions.Add(mission); await db.SaveChangesAsync();
        Check((await service.HistoryAsync(mission.Id, owner, false)).Count == 0, "existing mission without review records returns an empty history");
        var now = DateTimeOffset.UtcNow;
        var initial = new MissionUpdateRequest { Id = Guid.NewGuid(), MissionId = mission.Id, ActorId = owner, IsInitial = true,
            Status = "Approved", CreatedAt = now.AddHours(-2), ReviewedBy = manager, ReviewedAt = now.AddHours(-1),
            ProposedPlanJson = JsonSerializer.Serialize(new CreateMissionRequest { Name = "Initial plan" }) };
        var edit = new MissionUpdateRequest { Id = Guid.NewGuid(), MissionId = mission.Id, ActorId = owner,
            Status = "Rejected", CreatedAt = now, ReviewedBy = manager, ReviewedAt = now, Reason = "Retain current plan",
            BasePlanJson = "{\"name\":\"Initial plan\"}", ProposedPlanJson = "{\"name\":\"Proposed edit\"}" };
        var result = new MissionValidationResult { MissionId = mission.Id, Stage = "Creation" };
        var run = new MissionValidationRun { Id = result.Id, MissionId = mission.Id, UpdateRequestId = initial.Id,
            ResultJson = JsonSerializer.Serialize(result) };
        initial.ValidationRunId = run.Id;
        db.AddRange(initial, edit, run);
        db.MissionUpdateRequests.Add(new MissionUpdateRequest { Id = Guid.NewGuid(), MissionId = Guid.NewGuid(), ActorId = owner, CreatedAt = now.AddHours(1) });
        await db.SaveChangesAsync();
        var history = await service.HistoryAsync(mission.Id, owner, false);
        Check(history.Select(x => x.Id).SequenceEqual(new[] { edit.Id, initial.Id }), "history is newest first and excludes other missions");
        Check(history[0].BasePlan?.Name == "Initial plan" && history[0].ProposedPlan.Name == "Proposed edit", "stored snapshots accept camelCase and retain before/after plans");
        Check(history[0].ReviewedBy == manager && history[0].Reason == edit.Reason && history[0].Status == "Rejected", "reviewer, rejection and reason come from immutable review records");
        Check(history[1].IsInitial && history[1].BasePlan == null && history[1].Validation?.Id == result.Id, "initial submission retains its stored validation without revalidating");
        Check(history[0].Validation == null, "missing validation remains explicit without dropping its review record");
        Check((await service.HistoryAsync(mission.Id, manager, true)).Count == 2, "manager can inspect review history");
        try { await service.HistoryAsync(mission.Id, Guid.NewGuid(), false); throw new InvalidOperationException("Foreign mission leaked."); }
        catch (UnauthorizedAccessException) { Check(true, "another operator cannot read this mission's history"); }
        try { await service.HistoryAsync(Guid.NewGuid(), owner, false); throw new InvalidOperationException("Unknown mission accepted."); }
        catch (KeyNotFoundException) { Check(true, "nonexistent mission remains not found"); }
        var foreignRun = new MissionValidationRun { Id = Guid.NewGuid(), MissionId = Guid.NewGuid() };
        edit.ValidationRunId = foreignRun.Id; db.Add(foreignRun); await db.SaveChangesAsync();
        Check((await service.HistoryAsync(mission.Id, owner, false))[0].Validation == null, "validation link cannot expose a run from another mission");
        var services = new ServiceCollection(); services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(MissionEvidenceController).Assembly);
        using var provider = services.BuildServiceProvider();
        var actions = provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items.OfType<ControllerActionDescriptor>();
        Check(actions.Count(a => a.AttributeRouteInfo?.Template == "api/missions/{id:guid}/history") == 1, "MVC registers exactly one mission history route");
        Check(actions.Count(a => a.AttributeRouteInfo?.Template == "api/missions/automation/due") == 1, "absolute automation due route is registered without a mission-id prefix");
        Console.WriteLine($"Mission history checks passed: {count}");
    }
}
