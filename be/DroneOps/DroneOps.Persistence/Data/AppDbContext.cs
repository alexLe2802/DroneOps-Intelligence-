using DroneOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Persistence.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(
        DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<DroneHardwareIdentity> DroneHardwareIdentities { get; set; }
    public DbSet<DroneConnection> DroneConnections { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Pilot> Pilots { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<UAV> UAVs { get; set; }
    public DbSet<Mission> Missions { get; set; }
    public DbSet<Waypoint> Waypoints { get; set; }
    public DbSet<Geofence> Geofences { get; set; }
    public DbSet<Incident> Incidents { get; set; }
    public DbSet<MissionApproval> MissionApprovals { get; set; }
    public DbSet<TelemetryRecord> TelemetryRecords { get; set; }
    public DbSet<PilotRegistration> PilotRegistrations { get; set; }
    public DbSet<LicenseImage> LicenseImages { get; set; }
    public DbSet<MissionSuggestion> MissionSuggestions { get; set; }
    public DbSet<PostFlightReport> PostFlightReports { get; set; }
    public DbSet<FlightCompletionEvent> FlightCompletionEvents { get; set; }
    public DbSet<PilotUpdateRequest> PilotUpdateRequests { get; set; }

    public DbSet<MissionUpdateRequest> MissionUpdateRequests { get; set; }
    public DbSet<GeofenceRevision> GeofenceRevisions { get; set; }
    public DbSet<MissionWeatherCache> MissionWeatherCaches { get; set; }
    public DbSet<MissionValidationRun> MissionValidationRuns { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<MissionWeatherCache>().HasKey(x => x.Key);
        modelBuilder.Entity<DroneHardwareIdentity>().HasOne(h=>h.Uav).WithOne()
            .HasForeignKey<DroneHardwareIdentity>(h=>h.UavId).OnDelete(DeleteBehavior.Restrict);

        // Đảm bảo Email người dùng là duy nhất
        modelBuilder.Entity<User>()
            .HasIndex(x => x.Email)
            .IsUnique();

        // Ánh xạ chuẩn vào bảng Waypoint (dạng số ít) trong PostgreSQL
        modelBuilder.Entity<Waypoint>().ToTable("Waypoint");

        modelBuilder.Entity<PostFlightReport>()
            .HasIndex(x => new { x.MissionId, x.Revision })
            .IsUnique();
        modelBuilder.Entity<PostFlightReport>()
            .HasIndex(x => x.MissionId)
            .HasFilter("\"IsCurrent\" = TRUE")
            .IsUnique();
        modelBuilder.Entity<FlightCompletionEvent>()
            .HasIndex(x => x.IdempotencyKey)
            .IsUnique();
    }
}
