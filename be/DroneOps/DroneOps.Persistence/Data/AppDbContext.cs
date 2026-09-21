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

    public DbSet<User> Users { get; set; }

    public DbSet<Role> Roles { get; set; }

    public DbSet<UAV> UAVs { get; set; }

    public DbSet<Mission> Missions { get; set; }

    public DbSet<MissionVersion> MissionVersions { get; set; }

    public DbSet<Waypoint> Waypoints { get; set; }

    public DbSet<Geofence> Geofences { get; set; }

    public DbSet<Incident> Incidents { get; set; }

    public DbSet<MissionApproval> MissionApprovals { get; set; }

    public DbSet<TelemetryRecord> TelemetryRecords { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>()
            .HasIndex(x => x.Email)
            .IsUnique();

        modelBuilder.Entity<MissionVersion>()
            .HasIndex(x => new
            {
                x.MissionId,
                x.VersionNumber
            })
            .IsUnique();
    }
}