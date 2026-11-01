using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Odm.Agent.Os;

/// <summary>
/// Windows updates through the Windows Update Agent API (COM, Microsoft.Update.Session), called directly from C# with
/// late binding – no script and no extra process. Runs as SYSTEM; "SecurityOnly" installs security and critical updates.
/// </summary>
public sealed class WindowsUpdater(ILogger<WindowsUpdater> logger) : IOsUpdater
{
    private const string SearchCriteria = "IsInstalled=0 and IsHidden=0 and Type='Software'";
    private const int ResultSucceeded = 2; // OperationResultCode.orcSucceeded

    private static readonly string[] SecurityCategories = ["Security Updates", "Critical Updates"];

    public async Task<OsUpdateResult> RunAsync(OsUpdatePayload payload, string workDirectory, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Fail("Windows updates are only available on Windows.");
        }

        var timeout = TimeSpan.FromMinutes(Math.Clamp(payload.TimeoutMinutes, 5, 24 * 60));
        var securityOnly = payload.Mode == "SecurityOnly";
        var completion = new TaskCompletionSource<OsUpdateResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        // The Windows Update COM objects are used from a dedicated single-threaded apartment. The COM calls block and
        // cannot be cancelled, so a timeout abandons the (background) thread; the update service itself carries on.
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(Run(securityOnly));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "windows-update"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        try
        {
            return await completion.Task.WaitAsync(timeout, ct);
        }
        catch (TimeoutException)
        {
            return Fail($"Timed out after {timeout.TotalMinutes:N0} minutes.");
        }
        catch (COMException ex)
        {
            logger.LogWarning(ex, "Windows Update failed.");
            return Fail($"Windows Update failed (0x{ex.HResult:X8}). {ex.Message.Trim()}");
        }
    }

    [SupportedOSPlatform("windows")]
    private OsUpdateResult Run(bool securityOnly)
    {
        dynamic session = Create("Microsoft.Update.Session");
        dynamic searcher = session.CreateUpdateSearcher();
        dynamic found = searcher.Search(SearchCriteria);
        dynamic selected = Create("Microsoft.Update.UpdateColl");

        var titles = new List<string>();
        dynamic available = found.Updates;
        int availableCount = available.Count;
        for (var i = 0; i < availableCount; i++)
        {
            dynamic update = available.Item(i);
            if ((bool)update.InstallationBehavior.CanRequestUserInput)
            {
                continue; // needs a user; not possible unattended
            }

            if (securityOnly && !IsSecurity(CategoryNames(update)))
            {
                continue;
            }

            if (!(bool)update.EulaAccepted)
            {
                update.AcceptEula();
            }

            selected.Add(update);
            titles.Add((string)update.Title);
        }

        var installed = 0;
        var failed = 0;
        var reboot = false;
        int selectedCount = selected.Count;
        if (selectedCount > 0)
        {
            logger.LogInformation("Installing {Count} Windows update(s).", selectedCount);

            dynamic downloader = session.CreateUpdateDownloader();
            downloader.Updates = selected;
            downloader.Download();

            dynamic installer = session.CreateUpdateInstaller();
            installer.Updates = selected;
            dynamic result = installer.Install();
            reboot = (bool)result.RebootRequired;
            for (var i = 0; i < selectedCount; i++)
            {
                int code = result.GetUpdateResult(i).ResultCode;
                if (code == ResultSucceeded)
                {
                    installed++;
                }
                else
                {
                    failed++;
                }
            }
        }

        return new OsUpdateResult(
            failed == 0, installed, failed, titles, reboot || RebootPending(), failed > 0 ? $"{failed} update(s) could not be installed." : null);
    }

    [SupportedOSPlatform("windows")]
    private static object Create(string progId) =>
        Activator.CreateInstance(Type.GetTypeFromProgID(progId, throwOnError: true)!)!;

    [SupportedOSPlatform("windows")]
    private static List<string> CategoryNames(dynamic update)
    {
        var names = new List<string>();
        dynamic categories = update.Categories;
        int count = categories.Count;
        for (var i = 0; i < count; i++)
        {
            names.Add((string)categories.Item(i).Name);
        }

        return names;
    }

    /// <summary>Security and critical updates count as security updates.</summary>
    public static bool IsSecurity(IEnumerable<string> categoryNames) =>
        categoryNames.Any(n => SecurityCategories.Contains(n, StringComparer.Ordinal));

    [SupportedOSPlatform("windows")]
    private static bool RebootPending()
    {
        using var windowsUpdate = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
        using var servicing = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
        return windowsUpdate is not null || servicing is not null;
    }

    private static OsUpdateResult Fail(string message) => new(false, 0, 0, [], false, message);
}
