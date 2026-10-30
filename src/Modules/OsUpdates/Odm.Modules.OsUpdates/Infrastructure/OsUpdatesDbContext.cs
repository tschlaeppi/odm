using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Odm.Modules.OsUpdates.Domain;
using Odm.SharedKernel.Persistence;

namespace Odm.Modules.OsUpdates.Infrastructure;

public sealed class OsUpdatesDbContext(DbContextOptions<OsUpdatesDbContext> options) : DbContext(options)
{
    public const string Schema = "osupdates";

    public DbSet<UpdatePolicy> Policies => Set<UpdatePolicy>();
    public DbSet<MaintenanceWindow> Windows => Set<MaintenanceWindow>();
    public DbSet<OsAssignment> Assignments => Set<OsAssignment>();
    public DbSet<UpdateRun> Runs => Set<UpdateRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<UpdatePolicy>(b =>
        {
            b.ToTable("policies");
            b.HasKey(p => p.Id);
            b.Property(p => p.Name).HasMaxLength(128).IsRequired();
            b.HasIndex(p => p.Name).IsUnique();
            b.Property(p => p.Description).HasMaxLength(1024);
            b.Property(p => p.Mode).HasConversion<string>().HasMaxLength(16);
            b.Property(p => p.Reboot).HasConversion<string>().HasMaxLength(16);
        });

        modelBuilder.Entity<MaintenanceWindow>(b =>
        {
            b.ToTable("maintenance_windows");
            b.HasKey(w => w.Id);
            b.Property(w => w.Name).HasMaxLength(128).IsRequired();
            b.HasIndex(w => w.Name).IsUnique();
            b.Property(w => w.TimeZoneId).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<OsAssignment>(b =>
        {
            b.ToTable("assignments");
            b.HasKey(a => a.Id);
            b.Property(a => a.Scope).HasConversion<string>().HasMaxLength(16);
            b.Property(a => a.TargetKind).HasConversion<string>().HasMaxLength(16);
            b.Property(a => a.Target).HasMaxLength(64).IsRequired();
            b.HasIndex(a => new { a.Scope, a.ParentId, a.TargetKind, a.Target }).IsUnique();
        });

        modelBuilder.Entity<UpdateRun>(b =>
        {
            b.ToTable("runs");
            b.HasKey(r => r.Id);
            b.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
            b.Property(r => r.Message).HasMaxLength(UpdateRun.MaxMessageLength);
            b.HasIndex(r => new { r.DeviceId, r.WindowId, r.OccurrenceStart }).IsUnique();
            b.HasIndex(r => r.JobId);
            b.HasIndex(r => r.CreatedAt);
        });
    }
}

public sealed class OsUpdatesDbContextFactory : IDesignTimeDbContextFactory<OsUpdatesDbContext>
{
    public OsUpdatesDbContext CreateDbContext(string[] args) =>
        new(DatabaseExtensions.CreateDesignTimeOptions<OsUpdatesDbContext>(OsUpdatesDbContext.Schema));
}
