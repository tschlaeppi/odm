using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Odm.Modules.Agents.Domain;
using Odm.SharedKernel.Persistence;

namespace Odm.Modules.Agents.Infrastructure;

public sealed class AgentsDbContext(DbContextOptions<AgentsDbContext> options) : DbContext(options)
{
    public const string Schema = "agents";

    public DbSet<RegistrationToken> Tokens => Set<RegistrationToken>();
    public DbSet<AgentCredential> Credentials => Set<AgentCredential>();
    public DbSet<AgentJob> Jobs => Set<AgentJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<RegistrationToken>(b =>
        {
            b.ToTable("registration_tokens");
            b.HasKey(t => t.Id);
            b.Property(t => t.Label).HasMaxLength(128).IsRequired();
            b.Property(t => t.TokenHash).HasMaxLength(32).IsRequired();
            b.HasIndex(t => t.TokenHash).IsUnique();
        });

        modelBuilder.Entity<AgentCredential>(b =>
        {
            b.ToTable("agent_credentials");
            b.HasKey(c => c.Id);
            b.Property(c => c.SecretHash).HasMaxLength(32).IsRequired();
            b.HasIndex(c => c.DeviceId);
        });

        modelBuilder.Entity<AgentJob>(b =>
        {
            b.ToTable("agent_jobs");
            b.HasKey(j => j.Id);
            b.Property(j => j.Type).HasMaxLength(64).IsRequired();
            b.Property(j => j.Payload).HasMaxLength(4000);
            b.Property(j => j.Output).HasMaxLength(AgentJob.MaxOutputLength);
            b.Property(j => j.Status).HasConversion<string>().HasMaxLength(16);
            b.HasIndex(j => new { j.DeviceId, j.Status });
        });
    }
}

public sealed class AgentsDbContextFactory : IDesignTimeDbContextFactory<AgentsDbContext>
{
    public AgentsDbContext CreateDbContext(string[] args) =>
        new(DatabaseExtensions.CreateDesignTimeOptions<AgentsDbContext>(AgentsDbContext.Schema));
}
