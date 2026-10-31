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

- **Windows:** Windows Update Agent API (COM, no extra module) as SYSTEM. *Security only* = categories Security Updates and Critical Updates.
- **Linux (DEB):** `apt-get update`, then `upgrade`; for *security only* just packages from `*-security` sources.
- **Linux (RPM):** `dnf` or `yum` (`--security` for *security only*).
- **Restart:** only if the policy allows it, a restart is required and at least 5 minutes remain in the window. It is scheduled with
  a 2-minute delay so the agent can report the result first. A job that reaches the agent just before the window ends (device was offline) is not started.
- On Linux, if `shutdown` is refused because of an inhibitor (e.g. an open desktop session), the restart is scheduled with `systemd-run ... systemctl reboot --ignore-inhibitors`; the policy's "restart if required" therefore also applies to devices with logged-in users.
- On RPM systems restart detection needs `needs-restarting` (package `dnf-utils` / `yum-utils`).
- The server needs time zone data (`tzdata`); the default Debian based image contains it.
