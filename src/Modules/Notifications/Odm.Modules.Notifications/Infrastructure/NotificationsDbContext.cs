using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Odm.Modules.Notifications.Domain;
using Odm.SharedKernel.Persistence;

namespace Odm.Modules.Notifications.Infrastructure;

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public const string Schema = "notifications";

    public DbSet<SentAlert> Sent => Set<SentAlert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<SentAlert>(b =>
        {
            b.ToTable("sent_alerts");
            b.HasKey(a => a.Id);
            b.Property(a => a.Key).HasMaxLength(256).IsRequired();
            b.HasIndex(a => a.Key).IsUnique();
            b.HasIndex(a => a.SentAt);
            b.Property(a => a.Type).HasMaxLength(64).IsRequired();
            b.Property(a => a.DeviceName).HasMaxLength(256);
            b.Property(a => a.Subject).HasMaxLength(256).IsRequired();
            b.Property(a => a.Detail).HasMaxLength(1024).IsRequired();
            b.Property(a => a.Channels).HasMaxLength(64).IsRequired();
        });
    }
}

public sealed class NotificationsDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args) =>
        new(DatabaseExtensions.CreateDesignTimeOptions<NotificationsDbContext>(NotificationsDbContext.Schema));
}
