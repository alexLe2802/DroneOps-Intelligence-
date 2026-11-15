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
    public DbSet<MissionAutomationState> MissionAutomationStates { get; set; }
    public DbSet<MissionFlightExecution> MissionFlightExecutions { get; set; }
    public DbSet<MonitoringRuleRevision> MonitoringRuleRevisions { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Pilot> Pilots { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<UavReviewRequest> UavReviewRequests { get; set; }
    public DbSet<UAV> UAVs { get; set; }
    public DbSet<Mission> Missions { get; set; }
    public DbSet<Waypoint> Waypoints { get; set; }
    public DbSet<Geofence> Geofences { get; set; }
    public DbSet<Incident> Incidents { get; set; }
    public DbSet<IncidentTransition> IncidentTransitions { get; set; }
    public DbSet<IncidentAlertDelivery> IncidentAlertDeliveries { get; set; }
    public DbSet<MissionApproval> MissionApprovals { get; set; }
    public DbSet<TelemetryRecord> TelemetryRecords { get; set; }
    public DbSet<PilotRegistration> PilotRegistrations { get; set; }
    public DbSet<LicenseImage> LicenseImages { get; set; }
    public DbSet<MissionSuggestion> MissionSuggestions { get; set; }
    public DbSet<PostFlightReport> PostFlightReports { get; set; }
    public DbSet<FlightCompletionEvent> FlightCompletionEvents { get; set; }
    public DbSet<PilotUpdateRequest> PilotUpdateRequests { get; set; }
    public DbSet<PilotUpdateDecisionAudit> PilotUpdateDecisionAudits { get; set; }
    public DbSet<EmailChangeChallenge> EmailChangeChallenges { get; set; }
    public DbSet<AccountWorkflowAudit> AccountWorkflowAudits { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<AiAssessment> AiAssessments { get; set; }

    public DbSet<MissionUpdateRequest> MissionUpdateRequests { get; set; }
    public DbSet<GeofenceRevision> GeofenceRevisions { get; set; }
    public DbSet<MissionWeatherCache> MissionWeatherCaches { get; set; }
    public DbSet<MissionValidationRun> MissionValidationRuns { get; set; }

    private void AdvanceUavVersions()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<UAV>().Where(x => x.State == EntityState.Modified))
        {
            var version = entry.Property(x => x.RegistryVersion);
            if (version.CurrentValue == version.OriginalValue)
                version.CurrentValue = checked(version.OriginalValue + 1);
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AdvanceUavVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AdvanceUavVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<UAV>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<UAV>().Property(x => x.RegistryVersion).IsConcurrencyToken();
        modelBuilder.Entity<UavReviewRequest>().Property(x => x.Status).IsConcurrencyToken();
        modelBuilder.Entity<UavReviewRequest>().HasIndex(x => x.UavId).IsUnique()
            .HasFilter("\"Status\" = 'Pending'");
        modelBuilder.Entity<UavReviewRequest>().HasOne<UAV>().WithMany()
            .HasForeignKey(x => x.UavId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MissionUpdateRequest>().HasIndex(x => x.MissionId).IsUnique()
            .HasFilter("\"Status\" = 'Pending'").HasDatabaseName("UX_MissionUpdateRequest_Pending");
        modelBuilder.Entity<Incident>().Property(i => i.Revision).IsConcurrencyToken();
        modelBuilder.Entity<MissionFlightExecution>().HasIndex(e => e.UavId).IsUnique().HasFilter("\"CompletedAt\" IS NULL");
        modelBuilder.Entity<User>().Property(x=>x.ProfileVersion).IsConcurrencyToken();
        modelBuilder.Entity<PilotUpdateRequest>().Property(x => x.Status).IsConcurrencyToken();
        modelBuilder.Entity<AccountWorkflowAudit>().Property(x=>x.Id).ValueGeneratedOnAdd();
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
        modelBuilder.Entity<FlightCompletionEvent>()
            .HasIndex(x => x.MissionId)
            .HasFilter("\"IsTrusted\" = TRUE")
            .IsUnique();

        modelBuilder.Entity<IncidentTransition>()
            .HasOne(x => x.Actor)
            .WithMany()
            .HasForeignKey(x => x.ChangedBy)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<IncidentAlertDelivery>()
            .HasIndex(x => new { x.TransitionId, x.RecipientUserId })
            .IsUnique();
        modelBuilder.Entity<IncidentAlertDelivery>()
            .HasOne(x => x.Recipient)
            .WithMany()
            .HasForeignKey(x => x.RecipientUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Notification>().ToTable("Notification");
        modelBuilder.Entity<Notification>()
            .HasIndex(x => new { x.RecipientUserId, x.IsRead, x.CreatedAt })
            .IsDescending(false, false, true);
        modelBuilder.Entity<Notification>()
            .HasIndex(x => x.RecipientUserId)
            .HasFilter("\"Type\" = 'UavManualReview' AND \"ReferenceType\" = 'UAVManualReviewQueueActive'")
            .IsUnique();
        modelBuilder.Entity<Notification>()
            .HasOne(x => x.Recipient)
            .WithMany()
            .HasForeignKey(x => x.RecipientUserId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Notification>()
            .HasOne(x => x.Actor)
            .WithMany()
            .HasForeignKey(x => x.ActorUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
