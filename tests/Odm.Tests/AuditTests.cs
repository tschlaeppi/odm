using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Odm.Modules.Audit.Application;
using Odm.Modules.Audit.Domain;
using Odm.Modules.Audit.Infrastructure;
using Odm.SharedKernel.Contracts;
using Xunit;

namespace Odm.Tests;

public class AuditTests
{
    [Fact]
    public void Entry_fields_are_truncated()
    {
        var entry = AuditEntry.Create(DateTimeOffset.UtcNow, new string('a', 1000), "Admin", "DELETE x", new string('t', 1000),
            "success", 204, "10.0.0.1", new string('d', 5000));

        Assert.Equal(AuditEntry.MaxText, entry.Actor.Length);
        Assert.Equal(AuditEntry.MaxText, entry.Target!.Length);
        Assert.Equal(1024, entry.Details!.Length);
    }

    [Fact]
    public void Empty_actor_and_action_get_defaults()
    {
        var entry = AuditEntry.Create(DateTimeOffset.UtcNow, "", null, "", null, "", null, null, null);

        Assert.Equal("anonymous", entry.Actor);
        Assert.Equal("unknown", entry.Action);
    }

    [Fact]
    public void Actor_is_user_name_agent_or_anonymous()
    {
        ClaimsPrincipal With(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

        Assert.Equal("anna", AuditLog.ActorOf(With(new Claim("name", "anna"))));
        Assert.Equal("agent:42", AuditLog.ActorOf(With(new Claim("agent_id", "42"))));
        Assert.Equal("anonymous", AuditLog.ActorOf(With()));
        Assert.Equal("anonymous", AuditLog.ActorOf(null));
    }

    [Theory]
    [InlineData("/api/software/packages/{id}", true)]
    [InlineData("/api/agents/tokens", true)]
    [InlineData("/api/agent/v1/register", true)]
    [InlineData("/api/agent/v1/certificate", true)]
    [InlineData("/api/agent/v1/heartbeat", false)]
    [InlineData("/api/agent/v1/secrets/resolve", false)]
    [InlineData("/api/auth/login", false)]
    [InlineData("/api/groups/preview", false)]
    public void Only_relevant_routes_are_recorded(string route, bool expected) =>
        Assert.Equal(expected, AuditRules.ShouldRecord(route));

    [Fact]
    public async Task Retention_zero_keeps_everything()
    {
        var service = new AuditRetentionService(
            new NoScopes(), Options.Create(new AuditOptions { RetentionDays = 0 }), TimeProvider.System,
            NullLogger<AuditRetentionService>.Instance);

        Assert.Equal(0, await service.CleanAsync(default));
    }

    private sealed class NoScopes : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("No scope expected.");
    }
}
