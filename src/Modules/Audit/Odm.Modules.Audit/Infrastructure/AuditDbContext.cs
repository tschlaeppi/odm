using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Odm.Modules.Audit.Domain;
using Odm.SharedKernel.Persistence;

namespace Odm.Modules.Audit.Infrastructure;

public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public const string Schema = "audit";

    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<AuditEntry>(b =>
        {
            b.ToTable("audit_entries");
            b.HasKey(e => e.Id);
            b.HasIndex(e => e.Timestamp);
            b.Property(e => e.Actor).HasMaxLength(AuditEntry.MaxText).IsRequired();
            b.HasIndex(e => e.Actor);
            b.Property(e => e.Role).HasMaxLength(32);
            b.Property(e => e.Action).HasMaxLength(AuditEntry.MaxText).IsRequired();
            b.Property(e => e.Target).HasMaxLength(AuditEntry.MaxText);
            b.Property(e => e.Outcome).HasMaxLength(16).IsRequired();
            b.Property(e => e.IpAddress).HasMaxLength(64);
            b.Property(e => e.Details).HasMaxLength(1024);
        });
    }
}

public sealed class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args) =>
        new(DatabaseExtensions.CreateDesignTimeOptions<AuditDbContext>(AuditDbContext.Schema));
}
