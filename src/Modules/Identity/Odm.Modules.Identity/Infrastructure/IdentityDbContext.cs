using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Odm.Modules.Identity.Domain;
using Odm.SharedKernel.Persistence;

namespace Odm.Modules.Identity.Infrastructure;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public const string Schema = "identity";

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users");
            b.HasKey(u => u.Id);
            b.Property(u => u.UserName).HasMaxLength(128).IsRequired();
            b.HasIndex(u => u.UserName).IsUnique();
            b.Property(u => u.DisplayName).HasMaxLength(256).IsRequired();
            b.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
            b.Property(u => u.Role).HasConversion<string>().HasMaxLength(32);
        });
    }
}

/// <summary>Allows 'dotnet ef migrations add' without a running host.</summary>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args) =>
        new(DatabaseExtensions.CreateDesignTimeOptions<IdentityDbContext>(IdentityDbContext.Schema));
}
