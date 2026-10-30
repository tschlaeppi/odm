using Odm.Agent.Repo;
using Odm.Modules.Software.Domain;
using Xunit;

namespace Odm.Tests;

public class UninstallTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static Installation Installed()
    {
        var version = Guid.NewGuid();
        var installation = Installation.Create(Guid.NewGuid(), Guid.NewGuid(), version, Now);
        installation.MarkQueued(Guid.NewGuid(), Now);
        installation.Complete(true, "ok", Now);
        return installation;
    }

    [Fact]
    public void Removal_of_installed_package_is_queued_with_installed_version()
    {
        var installation = Installed();
        var installedVersion = installation.InstalledVersionId!.Value;

        Assert.True(installation.EvaluateRemoval(Now.AddMinutes(1)));

        Assert.True(installation.Removal);
        Assert.Equal(InstallStatus.Pending, installation.Status);
        Assert.Equal(installedVersion, installation.TargetVersionId);
    }

    [Fact]
    public void Successful_removal_marks_entry_as_removed()
    {
        var installation = Installed();
        installation.EvaluateRemoval(Now);
        installation.MarkQueued(Guid.NewGuid(), Now);

        installation.Complete(true, "done", Now.AddMinutes(2));

        Assert.Equal(InstallStatus.Removed, installation.Status);
        Assert.Null(installation.InstalledVersionId);
    }

    [Fact]
    public void Failed_removal_is_retried_later()
    {
        var installation = Installed();
        installation.EvaluateRemoval(Now);
        installation.MarkQueued(Guid.NewGuid(), Now);
        installation.Complete(false, "boom", Now);

        installation.EvaluateRemoval(Now.AddMinutes(5));
        Assert.Equal(InstallStatus.Failed, installation.Status);

        installation.EvaluateRemoval(Now.AddMinutes(20));
        Assert.Equal(InstallStatus.Pending, installation.Status);
        Assert.True(installation.Removal);
    }

    [Fact]
    public void Never_installed_package_has_nothing_to_remove()
    {
        var installation = Installation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        Assert.False(installation.EvaluateRemoval(Now));
    }

    [Fact]
    public void Switching_back_to_install_cancels_removal()
    {
        var installation = Installed();
        var target = Guid.NewGuid();
        installation.EvaluateRemoval(Now);

        installation.Evaluate(target, Now.AddMinutes(1));

        Assert.False(installation.Removal);
        Assert.Equal(InstallStatus.Pending, installation.Status);
        Assert.Equal(target, installation.TargetVersionId);
    }

    [Theory]
    [InlineData("Winget")]
    [InlineData("Chocolatey")]
    [InlineData("Apt")]
    [InlineData("Rpm")]
    [InlineData("Flatpak")]
    [InlineData("Snap")]
    public void Uninstall_arguments_name_the_package_and_are_unattended(string manager)
    {
        var payload = new InstallRepoPayload(manager, "pkg.id", null, null, null, null, null, false, null, 10, Uninstall: true);

        var args = manager switch
        {
            "Winget" => RepoInstaller.WingetUninstallArguments(payload),
            "Chocolatey" => RepoInstaller.ChocolateyUninstallArguments(payload),
            "Apt" => RepoInstaller.AptUninstallArguments(payload),
            "Rpm" => RepoInstaller.RpmUninstallArguments(payload),
            "Flatpak" => RepoInstaller.FlatpakUninstallArguments(payload),
            _ => RepoInstaller.SnapUninstallArguments(payload)
        };

        Assert.Contains("pkg.id", args);
        Assert.Contains(args, a => a is "uninstall" or "remove");
    }
}
