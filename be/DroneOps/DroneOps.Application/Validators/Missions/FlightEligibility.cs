using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;
namespace DroneOps.Application.Validators.Missions;

public static class FlightEligibility
{
    public static async Task LockAircraftAsync(AppDbContext db, Guid? id, CancellationToken ct)
    {
        if (id is null) throw new InvalidOperationException("A UAV must be allocated before approval/start.");
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is not null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"UAV\" WHERE \"Id\"={id.Value} FOR UPDATE", ct);
            // Account workflows lock User before Pilot; retain that order while dispatch/approval validates credentials.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"User\" WHERE \"Id\"=(SELECT \"UserId\" FROM \"Pilot\" WHERE \"Id\"=(SELECT \"PilotId\" FROM \"UAV\" WHERE \"Id\"={id.Value})) FOR SHARE", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Pilot\" WHERE \"Id\"=(SELECT \"PilotId\" FROM \"UAV\" WHERE \"Id\"={id.Value}) FOR SHARE", ct);
        }
    }
    public static async Task<string?> BlockingReasonAsync(AppDbContext db, Mission mission, CancellationToken ct)
    {
        var uav = await db.UAVs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mission.UAVId, ct);
        if (uav is null || uav.Status != "Active") return "An Active approved UAV is required.";
        var pilot = await db.Pilots.AsNoTracking().Include(x => x.User).SingleOrDefaultAsync(x => x.Id == uav.PilotId, ct);
        if (pilot?.User is null || pilot.UserId != mission.CreatedBy || !pilot.User.IsActive)
            return "The allocated Operator must be active and own this UAV.";
        if (mission.StartTime is null || mission.EndTime is null || string.IsNullOrWhiteSpace(pilot.PilotLicenseNo) ||
            pilot.LicenseIssuedDate is null || pilot.LicenseExpiredDate is null ||
            pilot.LicenseIssuedDate > DateOnly.FromDateTime(mission.StartTime.Value) ||
            pilot.LicenseExpiredDate < DateOnly.FromDateTime(mission.EndTime.Value))
            return "Approved flight credentials must cover the full planned mission window.";
        var conflicts = new[] { "Approved", "InProgress", "In Progress", "Active", "Flying", "Executing" };
        if (await db.Missions.AsNoTracking().AnyAsync(x => x.Id != mission.Id && conflicts.Contains(x.Status) &&
            (x.UAVId == mission.UAVId || x.CreatedBy == mission.CreatedBy) &&
            x.StartTime < mission.EndTime && x.EndTime > mission.StartTime, ct))
            return "UAV/Operator allocation overlaps another approved or active mission.";
        if (await db.MissionFlightExecutions.AsNoTracking().AnyAsync(x => x.UavId == mission.UAVId && x.MissionId != mission.Id && x.CompletedAt == null, ct))
            return "The UAV already has another active execution.";
        return null;
    }
}
