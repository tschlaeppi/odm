# Open Device Manager (ODM) – Requirements

## 1. Goal

Open Device Manager (ODM) is to become a free alternative to Microsoft Configuration Manager or Intune.
It supports IT in managing devices.

Core areas:

- Software distribution and lifecycle (updates)
- OS update management

## 2. Tech stack

### 2.1 Server

| Area | Requirement |
|---|---|
| Frontend | Web based, TypeScript |
| Backend | C# 10 |
| Database | PostgreSQL or Microsoft SQL Server |
| Deployment | Docker or Kubernetes |

### 2.2 Client (agent)

- **Programming language:** C# 10
- **Supported operating systems:**
  - Windows (server and client)
  - Linux (DEB and RPM based, server and client)

## 3. Functional requirements

### 3.1 Authentication

- Local database and/or authentication against LDAP or an OData endpoint
- Sign-in via OpenID Connect (e.g. Microsoft Entra ID, authentik); implemented instead of the OData endpoint

### 3.2 Device management

- Devices can be assigned to a primary user
- Devices can be assigned to a cost centre
- Devices can be grouped freely:
  - By name (filter: starts with, ends with, contains)
  - By OS and version
  - By organisational unit of the primary user

### 3.3 Software distribution

#### PSADT packages

- PSADT packages (zipped PSADT) can be uploaded to the system
- PSADT packages can be assigned to a device or a user
- New versions of PSADT packages can be distributed automatically or manually

#### Repository packages

Repository packages come from other repositories and can be defined in the system:

- Winget
- Chocolatey
- APT / DEB
- Flatpak
- Snap
- RPM

Further requirements:

- Repository packages can be pinned to a version or updated automatically by the package manager
- Depending on the repository, additional options can be given, e.g.:
  - Deploy Winget packages for the system or for the user
  - Unattended installation is mandatory
  - Optional installation arguments
- Additional arguments (also for PSADT) support secrets

#### Software profiles

- A software profile is a number of software packages that are installed in a defined order
- Software profiles can be assigned to a device or a user

#### Staged rollout

When a new package is assigned to a device or group, a staged rollout is possible:

- Via subgroups, or
- Via percentages, e.g. 20 % after 5 days, 40 % after 10 days, 100 % after 15 days

### 3.4 OS updates

- OS updates can be configured
- Maintenance windows can be defined for devices or groups

### 3.5 Agent installation

**As a system administrator** I want a simple agent installation procedure (download link or convenience script),
so that I do not have to compile every agent version myself.

Acceptance criteria:

- Ready-made, self-contained agent packages (Windows x64/ARM64, Linux x64/ARM64) are offered for download by the ODM server
  itself (also without internet access for the clients) and are also available as release artefacts
- One convenience script each for Windows (PowerShell) and Linux (sh) that downloads the package, verifies the checksum,
  sets up the service and registers the agent with a registration token
- After a token is created, the web UI shows the ready-made install command per operating system
- Running the script again updates the agent to the server's version (the registration is kept)
- The registration token is not kept on the device after successful registration

### 3.6 Agent self-update

**As a system administrator** I want agents to update themselves to the version provided by the server,
so that I do not have to update every device by hand.

Acceptance criteria:

- The server controls the target version (off / latest / pinned) and can release it in stages (percentage of devices, stable per device)
- Self-update is off by default
- The agent receives the offer in the heartbeat, downloads the package and verifies the SHA-256 checksum
- An update happens only when the agent is not processing jobs
- A helper process replaces the program file and restarts the service; if the new agent does not report back, the old version is restored and the failed version is not tried again
- Registration and credentials are kept; progress and errors are written to the agent's log file

### 3.7 Agent authentication with a client certificate (mTLS)

**As a system administrator** I want agents to identify themselves additionally with a client certificate,
so that a stolen agent secret alone is not enough.

Acceptance criteria:

- ODM runs its own CA; the private key of an agent never leaves the device (the agent sends only a certificate request)
- The certificate is issued at registration and renewed automatically before it expires
- The certificate is bound to the device's credentials (agent ID in the subject, thumbprint stored on the server)
- Modes: off (default) / optional (devices with a certificate must present it) / required
- An administrator can reset the certificate binding of a device
- A certificate is only issued over a connection on which it can later be presented (direct TLS), so enrolment cannot lock a device out

### 3.8 Uninstall, renaming, messages

- A package assignment has the action "install" or "uninstall"; uninstalling only happens where ODM installed the package
- Uninstall for PSADT (uninstall mode of the package) and all repository package managers; failed runs are retried a limited number of times
- Software packages can be renamed (the name stays unique)
- The full message of an installation can be read on the device and package pages (up to 64,000 characters, e.g. PSADT logs)

### 3.9 Dashboard

- Start page with key figures: devices, online status, devices without heartbeat (selectable threshold), failed installations
- Distributions by operating system and agent version; status of software distribution overall and per package
- List of the latest failures and of devices without heartbeat, linking to the device

### 3.10 HTTPS operation *(proposed)*

**As a system administrator** I want to run ODM securely over HTTPS with little effort.

Acceptance criteria:

- The Docker Compose setup offers an HTTPS variant (reverse proxy with automatic or provided certificates, or TLS in ODM itself)
- The documentation describes certificate handling and the required proxy settings (forwarded headers, upload limit)
- With a TLS-terminating reverse proxy, agent client certificates (mTLS) can still be used by forwarding the verified client certificate in a header from a trusted proxy

### 3.11 Audit log *(proposed)*

**As a security officer** I want to see who changed what and when.

Acceptance criteria:

- Security- and configuration-relevant actions are recorded: sign-in and failed sign-ins, creating / changing / deleting packages, assignments, profiles,
  policies, windows, groups and secrets, token creation, certificate reset, role-relevant actions
- Each entry contains time, user (or system), action, affected object and outcome; no secret values
- Administrators can search and filter the log in the UI; entries cannot be changed through the application
- Retention is configurable

### 3.12 Notifications *(proposed)*

- Alerts by e-mail and/or webhook for: failed installations after the last retry, devices without heartbeat beyond a threshold, failed OS update runs,
  agent certificates about to expire
- Recipients and events are configurable; alerts are sent once per event, not repeatedly

### 3.13 Housekeeping and backup *(proposed)*

- Configurable retention for finished jobs, update runs and audit entries; a background job removes old data
- The documentation describes backup and restore of database, package storage, secrets key and agent CA

### 3.14 Inventory and reporting *(proposed)*

- Hardware and software inventory per device (installed programs and versions, not only packages distributed by ODM)
- Reports and CSV export, e.g. devices per installed software and version, devices without a required package
- Compliance view: devices that deviate from their assignments

### 3.15 Kubernetes *(proposed)*

- A Helm chart deploys ODM on Kubernetes with an external or bundled database and S3 store, ingress with TLS and secrets from the cluster
- Health checks and several replicas are supported (stateless server; the agent CA is kept in shared storage or a secret)

### 3.16 Operation as a user *(proposed)*

- Package installation in the context of the signed-in user (e.g. Winget user scope), in addition to the system context

## 4. Non-functional requirements

- **Design:** clean, minimalist, dark mode support
- **Security:** passwords must be hashed
- **Performance:** no heavy external libraries unless necessary
- **Multilingual (i18n):** German and English from the start; every UI text exists in both languages
- **Interfaces:** REST between API and UI and between API and agent
- **Data storage:** PSADT packages are stored on S3 storage
- **Coding conventions:** cleanly object oriented and modular; start as a modular monolith
- **Languages:** documentation, log messages, API messages and code comments are English; the UI is German and English
