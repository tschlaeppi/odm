# OS updates

1. **OS updates** → create a **policy** (*security updates only* or *all*; restart *never* or *if required*) and assign it to a group
   or device. Each target has exactly one policy; a device takes precedence over its group.
2. Create **maintenance windows**: weekdays, start, duration (15 minutes to 24 hours, also across midnight) and time zone (DST is
   handled). Assign windows to devices or groups. Windows assigned directly to a device replace the windows of its groups;
   several group windows add up.
3. The server checks every minute (`OsUpdates:CheckSeconds`) whether a window is open for a device with a policy and gives the agent
   at most one job per device and window. **Without a policy or window no updates are installed automatically.**
4. **Update runs** shows result, count, errors and whether a restart is required or scheduled. For testing, a device's *Jobs* dialog has
   **Run updates now**, which ignores the window but not the policy.

## How the agent updates

- **Windows:** Windows Update Agent API (COM, called directly from the agent without a script or an extra process) as SYSTEM. *Security only* = categories Security Updates and Critical Updates.
- **Linux (DEB):** `apt-get update`, then `upgrade`; for *security only* just packages from `*-security` sources.
- **Linux (RPM):** `dnf` or `yum` (`--security` for *security only*).
- **Restart:** only if the policy allows it, a restart is required and at least 5 minutes remain in the window. It is scheduled with
  a 2-minute delay so the agent can report the result first. A job that reaches the agent just before the window ends (device was offline) is not started.
- On Linux, if `shutdown` is refused because of an inhibitor (e.g. an open desktop session), the restart is scheduled with `systemd-run ... systemctl reboot --ignore-inhibitors`; the policy's "restart if required" therefore also applies to devices with logged-in users.
- On RPM systems restart detection needs `needs-restarting` (package `dnf-utils` / `yum-utils`).
- The server needs time zone data (`tzdata`); the default Debian based image contains it.

## Update history

Besides the runs, the individual updates of each run are stored: the title of a Windows update, or `package old → new` on Linux (DEB), `package version` on RPM systems.
They are shown in the **Update history** section of the *OS updates* page (all devices) and on each device page, with a search field.
Up to 500 entries per run are reported by the agent and up to 1000 are kept; the run itself always shows the exact counts.
The history is deleted together with its run after `OsUpdates:RunRetentionDays`. Runs from before this feature have no entries.

Updates that were **not installed by ODM** (manually, or by Windows Update or `unattended-upgrades` on their own) are listed as well and marked
*Not via ODM*. The agent reads them from the operating system together with the inventory (every `Agent:InventoryHours`):
Windows from the Windows Update history (successful installations), Linux/DEB from `/var/log/dpkg.log*` (upgrades only).
RPM based systems are not supported yet. Entries that fall into the time span of an ODM run count as installed by ODM and are not listed twice.
Only the last `OsUpdates:RunRetentionDays` days are kept.
