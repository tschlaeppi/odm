using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Odm.Modules.Software.Domain;
using Odm.SharedKernel.Persistence;

namespace Odm.Modules.Software.Infrastructure;

public sealed class SoftwareDbContext(DbContextOptions<SoftwareDbContext> options) : DbContext(options)
{
    public const string Schema = "software";

    public DbSet<SoftwarePackage> Packages => Set<SoftwarePackage>();
    public DbSet<PackageVersion> Versions => Set<PackageVersion>();
    public DbSet<PackageDependency> Dependencies => Set<PackageDependency>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<Installation> Installations => Set<Installation>();
    public DbSet<SoftwareSecret> Secrets => Set<SoftwareSecret>();
    public DbSet<SoftwareProfile> Profiles => Set<SoftwareProfile>();
    public DbSet<ProfileItem> ProfileItems => Set<ProfileItem>();
    public DbSet<ProfileAssignment> ProfileAssignments => Set<ProfileAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<SoftwarePackage>(b =>
        {
            b.ToTable("packages");
            b.HasKey(p => p.Id);
            b.Property(p => p.Name).HasMaxLength(128).IsRequired();
            b.HasIndex(p => p.Name).IsUnique();
            b.Property(p => p.Description).HasMaxLength(1024);
            b.Property(p => p.Kind).HasConversion<string>().HasMaxLength(16);
            b.Property(p => p.SpecJson).HasMaxLength(4000);
        });

        modelBuilder.Entity<PackageVersion>(b =>
        {
            b.ToTable("package_versions");
            b.HasKey(v => v.Id);
            b.Property(v => v.Id).ValueGeneratedNever();
            b.Property(v => v.Version).HasMaxLength(64).IsRequired();
            b.Property(v => v.ObjectKey).HasMaxLength(256);
            b.Property(v => v.Sha256).HasMaxLength(64);
            b.Property(v => v.SpecSnapshot).HasMaxLength(4000);
            b.Property(v => v.Notes).HasMaxLength(1024);
            b.HasIndex(v => new { v.PackageId, v.Version }).IsUnique();
            b.HasOne<SoftwarePackage>().WithMany().HasForeignKey(v => v.PackageId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PackageDependency>(b =>
        {
            b.ToTable("package_dependencies");
            b.HasKey(d => d.Id);
            b.HasIndex(d => new { d.PackageId, d.DependsOnId }).IsUnique();
            b.HasIndex(d => d.DependsOnId);
            b.HasOne<SoftwarePackage>().WithMany().HasForeignKey(d => d.PackageId).OnDelete(DeleteBehavior.Cascade);
            // No second cascade path (SQL Server): rows that point at a deleted package are removed by the delete endpoint.
            b.HasOne<SoftwarePackage>().WithMany().HasForeignKey(d => d.DependsOnId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Assignment>(b =>
        {
            b.ToTable("assignments");
            b.HasKey(a => a.Id);
            b.Property(a => a.TargetKind).HasConversion<string>().HasMaxLength(16);
            b.Property(a => a.Target).HasMaxLength(256).IsRequired();
            b.Property(a => a.VersionPolicy).HasConversion<string>().HasMaxLength(16);
            b.Property(a => a.Intent).HasConversion<string>().HasMaxLength(16).HasDefaultValue(AssignmentIntent.Install);
            b.Property(a => a.RolloutJson).HasMaxLength(2000);
            b.HasIndex(a => new { a.PackageId, a.TargetKind, a.Target }).IsUnique();
            b.HasOne<SoftwarePackage>().WithMany().HasForeignKey(a => a.PackageId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SoftwareProfile>(b =>
        {
            b.ToTable("profiles");
            b.HasKey(p => p.Id);
            b.Property(p => p.Name).HasMaxLength(128).IsRequired();
            b.HasIndex(p => p.Name).IsUnique();
            b.Property(p => p.Description).HasMaxLength(1024);
        });

        modelBuilder.Entity<ProfileItem>(b =>
        {
            b.ToTable("profile_items");
            b.HasKey(i => i.Id);
            b.Property(i => i.VersionPolicy).HasConversion<string>().HasMaxLength(16);
            b.HasIndex(i => new { i.ProfileId, i.PackageId }).IsUnique();
            b.HasIndex(i => new { i.ProfileId, i.Position });
            b.HasOne<SoftwareProfile>().WithMany().HasForeignKey(i => i.ProfileId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<SoftwarePackage>().WithMany().HasForeignKey(i => i.PackageId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProfileAssignment>(b =>
        {
            b.ToTable("profile_assignments");
            b.HasKey(a => a.Id);
            b.Property(a => a.TargetKind).HasConversion<string>().HasMaxLength(16);
            b.Property(a => a.Target).HasMaxLength(256).IsRequired();
            b.Property(a => a.RolloutJson).HasMaxLength(2000);
            b.HasIndex(a => new { a.ProfileId, a.TargetKind, a.Target }).IsUnique();
            b.HasOne<SoftwareProfile>().WithMany().HasForeignKey(a => a.ProfileId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SoftwareSecret>(b =>
        {
            b.ToTable("secrets");
            b.HasKey(s => s.Id);
            b.Property(s => s.Name).HasMaxLength(64).IsRequired();
            b.HasIndex(s => s.Name).IsUnique();
            b.Property(s => s.Ciphertext).HasMaxLength(8192).IsRequired();
        });

        modelBuilder.Entity<Installation>(b =>
        {
            b.ToTable("installations");
            b.HasKey(i => i.Id);
            b.Property(i => i.Status).HasConversion<string>().HasMaxLength(16);
            b.Property(i => i.Removal).HasDefaultValue(false);
            b.Property(i => i.Message).HasMaxLength(Installation.MaxMessageLength);
            b.HasIndex(i => new { i.DeviceId, i.PackageId }).IsUnique();
            b.HasIndex(i => i.JobId);
            b.HasOne<SoftwarePackage>().WithMany().HasForeignKey(i => i.PackageId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class SoftwareDbContextFactory : IDesignTimeDbContextFactory<SoftwareDbContext>
{
    public SoftwareDbContext CreateDbContext(string[] args) =>
        new(DatabaseExtensions.CreateDesignTimeOptions<SoftwareDbContext>(SoftwareDbContext.Schema));
}
