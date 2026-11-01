# Software distribution

ODM keeps a *desired state* (assignments) and an *actual state* (installations per device and package) and reconciles them
every minute (`Software:ReconcileSeconds`) and immediately after changes. New group members, new package versions and
auto-updating repository packages are picked up automatically. Installation is an agent job (silent, as SYSTEM/root).

## PSADT packages

1. **Software** → create a package → upload a ZIP with the PSADT package as a new version. The ZIP must contain
   `Invoke-AppDeployToolkit.ps1` (PSADT v4) or `Deploy-Application.ps1` (v3) in its root or one level below.
   The agent runs it with `-DeploymentType Install -DeployMode Silent` (`Uninstall` for removal).
2. Assign the package to a group, device or user (primary user). *Latest* distributes new versions automatically,
   *Pinned* only the chosen version.
3. Status per device is shown under *Installations*. Failed installations are retried up to 3 times (every 15 minutes),
   then *Retry* restarts them.

Notes:

- The agent downloads the package through the ODM server (not directly from S3); the server checks that the device has a
  job for exactly that version. The agent verifies the SHA-256 checksum before unpacking.
- Packages are unpacked in a protected working directory (not `%TEMP%`) because they run as SYSTEM.
- When a package is assigned to a device several times, the most specific source wins: device before user before group.
- PSADT packages have a **platform** (Windows or Linux, chosen when the package is created and not changeable afterwards).
  Windows packages run with Windows PowerShell 5.1 on Windows devices. Exit codes 0, 1641 and 3010 count as success.
- **PSADT on Linux** uses [PSAppDeployToolkit-Linux](https://github.com/tschlaeppi/PSAppDeployToolkit-Linux): upload a ZIP that is
  a deployment package of that project (it contains `Invoke-AppDeployToolkit.ps1` and the module, e.g. the `Template/` folder with
  your install logic). The device needs PowerShell 7.4 or newer (`pwsh`, in `/usr/bin`, `/usr/local/bin`, `/opt/microsoft/powershell/7` or
  `/snap/bin`); the agent runs the script as root with `-DeploymentType Install|Uninstall -DeployMode Silent`. Linux passes only the low 8 bits
  of an exit code, so 3010 and 1641 (restart) arrive as 194 and 105; the agent treats 0, 105 and 194 as success.
- Maximum package size: `Software:MaxPackageMegabytes` (default 4096). Behind a reverse proxy its upload limit must match.

## Repository packages

Winget, Chocolatey, APT, RPM, Flatpak and Snap. ODM stores only the configuration; the device's package manager does the work.

- **Fixed or automatic version:** with a version entered exactly that version is installed. With the field empty the package
  manager installs the latest version and ODM has it refreshed every `Software:RepoRefreshHours` (default 24, 0 = never).
  Changing the settings creates a new *revision*; all assigned devices are reconciled to it.
- **Always unattended:** Winget `--silent --disable-interactivity`, Chocolatey `-y`, APT/DNF `-y`, Flatpak `--noninteractive`.
- **Per manager:** Winget (scope `machine`/`user`, source), Chocolatey (source), Flatpak (scope, remote; *version* = commit ID),
  Snap (channel, classic; *version* = revision), APT and RPM (name, version).
- **Matching devices:** Winget/Chocolatey and PSADT packages of platform Windows only on Windows, the rest (incl. PSADT packages of platform Linux) only on Linux. APT packages go to DEB based, RPM
  packages to RPM based devices (detected by OS name; unknown distributions are allowed and the agent reports a clear error if the
  package manager is missing).
- **Additional arguments** are appended to the command (split like a shell would, but without a shell). For PSADT they are passed to the script,
  which must declare the parameters. They apply to installation only, not to uninstallation.

**Scope = user (Winget, Flatpak)** installs for the signed-in user, not for the agent's account:

- Windows: the agent starts winget in the session of the user signed in at the console (the user's own App Installer, no elevation).
- Linux: the agent runs Flatpak as the user of the first graphical or console session (`runuser`, `--user` installation).
- Nobody signed in (or only remote sessions): the job fails with a clear message and is retried by the reconciler; it succeeds
  once someone is signed in. With several users on one device only the active console user is served.
- Programs installed for a single user do not appear in the inventory (machine-wide programs only).

Known limits:

- Winget belongs to the App Installer (MSIX) and is not available to the SYSTEM account everywhere; the agent searches `WindowsApps`.
- A fixed version **older** than the installed one is only applied by Chocolatey and APT (downgrade allowed). For Winget, Snap and
  Flatpak uninstall first.

## Secrets

**Software → Secrets** (create and change: administrators only). In arguments write `{{secret:NAME}}`. The value is stored
encrypted (AES-256-GCM), never shown and not shipped with the job: the agent fetches it at execution time, only for secrets
the running job uses, and removes secret values from the output before reporting.

Limits: during installation the value is on the installer's command line (visible to local administrators), and anyone who may
edit packages can pass a secret to arbitrary arguments. Treat secrets as credentials that operators may *use*, not *read*.

## Software profiles

A profile is an ordered list of packages (PSADT and repository packages mixed).

- Assign a profile to a device, user or group. On each device a package starts only when all packages before it are installed.
  If a package fails for good (3 attempts), the following ones wait until *Retry* succeeds.
- Packages that do not fit a device (e.g. Winget on Linux) are skipped and do not block the order.
- When the same package comes from several sources, the more specific wins: device before user before group; for the same
  target a single package assignment before a profile.
- Profile items of PSADT packages may pin a version.
- The device page shows every profile that applies to the device (directly, via its user or via a group) below the software table, with the
  state of each package: the installation status, or *Scheduled* (rollout stage not reached yet), *Not applicable* (other platform or
  distribution) or *No version available*.

## Rollouts in stages

Package and profile assignments can be rolled out in stages; the time of the assignment is the start.

- *By percentage*, e.g. 20 % after 5 days, 40 % after 10, 100 % after 15. Which devices belong to which stage is fixed per
  assignment and device (stable hash), so the selection only grows. The last stage must be 100 %. Before the first stage no device gets the package.
- *By subgroups*: each chosen group is released after its delay. Target devices in none of the subgroups follow after the last stage.
- Stages apply to **new** installations only. Devices that already have the package keep being updated, and new package versions
  are not staged again.
- The state is visible per assignment ("stage 2/3 · 40 % released"). To change a plan, recreate the assignment (the API can also replace it with `PUT`).

## Uninstalling

An assignment has an *action*: **Install** or **Uninstall**. With *Uninstall* ODM removes the package from the target devices
**where ODM installed it**:

- PSADT: the package ZIP of the installed version runs with `-DeploymentType Uninstall`.
- Repository packages: Winget `uninstall`, Chocolatey `uninstall`, `apt-get remove`, `dnf|yum remove`, `flatpak uninstall`,
  `snap remove`.
- Failed removals are retried up to 3 times. After success the installation entry disappears.
- Switching the assignment back to *Install* cancels a pending removal and installs again.
- If a device has both an install and an uninstall source with the same specificity, install wins.
- Uninstall applies to individual package assignments, not to profiles. Deleting an assignment or a package does **not**
  uninstall anything.

## Package names and messages

Packages can be renamed on the package page (names stay unique). The full result message of an installation (up to
64 000 characters, the end of the log) is available via *Show* on the device and package pages.
