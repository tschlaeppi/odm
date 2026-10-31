using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Odm.Modules.Devices.Domain;
using Odm.SharedKernel.Persistence;

namespace Odm.Modules.Devices.Infrastructure;

public sealed class DevicesDbContext(DbContextOptions<DevicesDbContext> options) : DbContext(options)
{
    public const string Schema = "devices";

    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceGroup> Groups => Set<DeviceGroup>();
    public DbSet<DeviceHardware> Hardware => Set<DeviceHardware>();
    public DbSet<InstalledSoftware> Software => Set<InstalledSoftware>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Device>(b =>
        {
            b.ToTable("devices");
            b.HasKey(d => d.Id);
            b.Property(d => d.Name).HasMaxLength(256).IsRequired();
            b.HasIndex(d => d.Name);
            b.Property(d => d.OsFamily).HasConversion<string>().HasMaxLength(32);
            b.Property(d => d.OsName).HasMaxLength(128).IsRequired();
            b.Property(d => d.OsVersion).HasMaxLength(64).IsRequired();
            b.Property(d => d.PrimaryUser).HasMaxLength(256);
            b.Property(d => d.PrimaryUserOrgUnit).HasMaxLength(256);
            b.Property(d => d.CostCenter).HasMaxLength(64);
            b.Property(d => d.AgentVersion).HasMaxLength(32);
        });

        modelBuilder.Entity<DeviceHardware>(b =>
        {
            b.ToTable("device_hardware");
            b.HasKey(h => h.DeviceId);
            b.Property(h => h.Manufacturer).HasMaxLength(128);
            b.Property(h => h.Model).HasMaxLength(128);
            b.Property(h => h.Cpu).HasMaxLength(256);
            b.Property(h => h.Architecture).HasMaxLength(32);
            b.Property(h => h.LastUser).HasMaxLength(256);
            b.Property(h => h.NetworkJson).HasMaxLength(8000);
            b.Property(h => h.DisksJson).HasMaxLength(8000);
            // Computed views of the JSON columns, not mapped (EF would take them for navigations)
            b.Ignore(h => h.Adapters);
            b.Ignore(h => h.Volumes);
        });

        modelBuilder.Entity<InstalledSoftware>(b =>
        {
            b.ToTable("installed_software");
            b.HasKey(s => s.Id);
            b.HasIndex(s => s.DeviceId);
            b.HasIndex(s => s.Name);
            b.Property(s => s.Name).HasMaxLength(InstalledSoftware.MaxName).IsRequired();
            b.Property(s => s.Version).HasMaxLength(128);
            b.Property(s => s.Publisher).HasMaxLength(256);
        });

        modelBuilder.Entity<DeviceGroup>(b =>
        {
            b.ToTable("device_groups");
            b.HasKey(g => g.Id);
            b.Property(g => g.Name).HasMaxLength(128).IsRequired();
            b.HasIndex(g => g.Name).IsUnique();
            b.Property(g => g.Description).HasMaxLength(1024);
            b.Property(g => g.MatchMode).HasConversion<string>().HasMaxLength(16);

            b.OwnsMany(g => g.Rules, r =>
            {
                r.ToTable("device_group_rules");
                r.Property(x => x.Field).HasConversion<string>().HasMaxLength(32);
                r.Property(x => x.Operator).HasConversion<string>().HasMaxLength(16);
                r.Property(x => x.Value).HasMaxLength(256).IsRequired();
            });
            b.Navigation(g => g.Rules).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}

public sealed class DevicesDbContextFactory : IDesignTimeDbContextFactory<DevicesDbContext>
{
    public DevicesDbContext CreateDbContext(string[] args) =>
        new(DatabaseExtensions.CreateDesignTimeOptions<DevicesDbContext>(DevicesDbContext.Schema));
}
