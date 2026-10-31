using Odm.Modules.Software.Domain;

namespace Odm.Tests;

/// <summary>Der Zustandsautomat einer Installation: Pending, Running, Installed, Failed.</summary>
public class InstallationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid V1 = Guid.NewGuid();
    private static readonly Guid V2 = Guid.NewGuid();

    private static Installation NewInstalled(DateTimeOffset at)
    {
        var installation = Installation.Create(Guid.NewGuid(), Guid.NewGuid(), V1, at);
        installation.MarkQueued(Guid.NewGuid(), at);
        installation.Complete(success: true, "ok", at);
        return installation;
    }

    [Fact]
    public void Happy_path()
    {
        var installation = Installation.Create(Guid.NewGuid(), Guid.NewGuid(), V1, T0);
        Assert.Equal(InstallStatus.Pending, installation.Status);

        installation.MarkQueued(Guid.NewGuid(), T0);
        Assert.Equal(InstallStatus.Running, installation.Status);
        Assert.Equal(V1, installation.JobVersionId);

        installation.Complete(success: true, null, T0.AddMinutes(5));
        Assert.Equal(InstallStatus.Installed, installation.Status);
        Assert.Equal(V1, installation.InstalledVersionId);
    }

    [Fact]
    public void Installed_stays_installed_while_target_is_unchanged()
    {
        var installation = NewInstalled(T0);

        installation.Evaluate(V1, T0.AddDays(30));

        Assert.Equal(InstallStatus.Installed, installation.Status);
    }

    [Fact]
    public void New_target_version_requeues_an_installed_package()
    {
        var installation = NewInstalled(T0);

        installation.Evaluate(V2, T0.AddMinutes(1));

        Assert.Equal(InstallStatus.Pending, installation.Status);
        Assert.Equal(V2, installation.TargetVersionId);
        Assert.Null(installation.JobId);
    }

    [Fact]
    public void Failed_install_is_retried_after_a_pause_but_not_before()
    {
        var installation = Installation.Create(Guid.NewGuid(), Guid.NewGuid(), V1, T0);
        installation.MarkQueued(Guid.NewGuid(), T0);
        installation.Complete(success: false, "Fehler", T0);

        installation.Evaluate(V1, T0.AddMinutes(14));
        Assert.Equal(InstallStatus.Failed, installation.Status);

        installation.Evaluate(V1, T0.AddMinutes(16));
        Assert.Equal(InstallStatus.Pending, installation.Status);
    }

    [Fact]
    public void Automatic_retries_stop_after_three_attempts()
    {
        var installation = Installation.Create(Guid.NewGuid(), Guid.NewGuid(), V1, T0);
        var now = T0;

        for (var attempt = 1; attempt <= Installation.MaxAttempts; attempt++)
        {
            installation.MarkQueued(Guid.NewGuid(), now);
            installation.Complete(success: false, $"Fehler {attempt}", now);
            now = now.AddMinutes(16);
            installation.Evaluate(V1, now);
        }

        Assert.Equal(Installation.MaxAttempts, installation.Attempts);
        Assert.Equal(InstallStatus.Failed, installation.Status); // permanent until someone chooses "Retry"

        installation.Retry(now);
        Assert.Equal(InstallStatus.Pending, installation.Status);
        Assert.Equal(0, installation.Attempts);
    }

    [Fact]
    public void A_new_target_resets_a_failed_installation()
    {
        var installation = Installation.Create(Guid.NewGuid(), Guid.NewGuid(), V1, T0);
        installation.MarkQueued(Guid.NewGuid(), T0);
        installation.Complete(success: false, "Fehler", T0);

        installation.Evaluate(V2, T0.AddSeconds(1));

        Assert.Equal(InstallStatus.Pending, installation.Status);
        Assert.Equal(0, installation.Attempts);
    }

    [Fact]
    public void Running_job_that_never_reports_is_requeued_after_the_timeout()
    {
        var installation = Installation.Create(Guid.NewGuid(), Guid.NewGuid(), V1, T0);
        installation.MarkQueued(Guid.NewGuid(), T0);

        installation.Evaluate(V1, T0.AddHours(5).AddMinutes(59));
        Assert.Equal(InstallStatus.Running, installation.Status);

        installation.Evaluate(V1, T0.AddHours(6));
        Assert.Equal(InstallStatus.Pending, installation.Status);
    }

    [Fact]
    public void Auto_updating_repo_packages_are_refreshed_periodically()
    {
        var installation = NewInstalled(T0);
        var refreshAfter = TimeSpan.FromHours(24);

        installation.Evaluate(V1, T0.AddHours(23), refreshAfter);
        Assert.Equal(InstallStatus.Installed, installation.Status);

        installation.Evaluate(V1, T0.AddHours(25), refreshAfter);
        Assert.Equal(InstallStatus.Pending, installation.Status);
    }

    [Fact]
    public void Without_refresh_interval_an_installed_package_is_left_alone()
    {
        var installation = NewInstalled(T0);

        installation.Evaluate(V1, T0.AddDays(400), refreshAfter: null);

        Assert.Equal(InstallStatus.Installed, installation.Status);
    }

    [Fact]
    public void Long_output_is_cut_to_the_end()
    {
        var installation = Installation.Create(Guid.NewGuid(), Guid.NewGuid(), V1, T0);
        installation.MarkQueued(Guid.NewGuid(), T0);

        installation.Complete(success: false, new string('a', Installation.MaxMessageLength + 1000) + "ENDE", T0);

        Assert.Equal(Installation.MaxMessageLength, installation.Message!.Length);
        Assert.EndsWith("ENDE", installation.Message);
    }
}
