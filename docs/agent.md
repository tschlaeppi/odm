# Agent

The agent is a self-contained .NET worker service for Windows (x64, ARM64) and Linux (x64, ARM64; DEB and RPM based).
It registers once with a one-time token, then polls the server (heartbeat), runs jobs and reports results. The server never
connects to the agent.

## Installation

ODM serves prebuilt agent packages and install scripts itself, so clients need no internet access. In **Enrollment**
create a one-time token; the page then shows the ready-to-copy command:

```powershell
# Windows (PowerShell as administrator)
& ([scriptblock]::Create((irm 'https://odm.example.org/api/agent/install.ps1'))) -Token '<token>'
```

```bash
# Linux (root)
curl -fsSL 'https://odm.example.org/api/agent/install.sh' | sudo sh -s -- --token '<token>'
```

The scripts detect the architecture, verify the SHA-256 checksum of the download, install the agent
(`C:\Program Files\OpenDeviceManager` or `/opt/odm-agent`), set up a service (restart on failure) and start it.
Running a script again updates the agent to the server's version; the registration is kept (no token needed then).
The token is only briefly stored in `agent.json` in the protected state directory and removed by the agent after registration.
Without HTTPS scripts and agent refuse to connect (testing only: `-AllowInsecureHttp` / `--allow-insecure-http`).
Direct downloads with checksums: `GET /api/agent/packages`, `GET /api/agent/download/{platform}`.

### Providing the agent packages

- **Docker image:** `docker build` builds the packages (stage `agent`, four platforms) into `/app/agent-packages`.
  Faster without packages: `--build-arg AGENT_RIDS=""`.
- **Without Docker:** `scripts/publish-agent.sh` (or `.ps1`) creates `odm-agent-<version>-<rid>.zip|tar.gz` in `agent-packages/`.
  Alternatively put the packages of a GitHub release (workflow `release-agent.yml`, triggered by tags `v*`) into that directory.
- Settings: `Agents:PackagesPath` and `Agents:PublicBaseUrl`. The highest version per platform is offered, so updating the
  server is enough to roll out new agents.

### Manual installation

```bash
dotnet publish src/Agent/Odm.Agent -c Release -r linux-x64 --self-contained -o /opt/odm-agent
install -d -m 700 /var/lib/odm-agent
printf '{"Agent":{"ServerUrl":"https://odm.example.org","RegistrationToken":"<token>"}}' > /var/lib/odm-agent/agent.json
chmod 600 /var/lib/odm-agent/agent.json
cp deploy/odm-agent.service /etc/systemd/system/ && systemctl daemon-reload && systemctl enable --now odm-agent
```

Credentials live in `C:\ProgramData\OpenDeviceManager` (SYSTEM / administrators only) or `/var/lib/odm-agent` (0700/0600).

## Self-update

Disabled by default. Enable it in the server configuration (restart required):

```yaml
Agents__Update__Mode: Latest       # Off | Latest | Pinned
Agents__Update__TargetVersion: ""  # only for Pinned, e.g. 0.4.2 (also allows downgrades)
Agents__Update__RolloutPercent: 10 # share of devices (stable per device); raise step by step to 100
```

The agent learns the target version from the heartbeat, downloads the package from `agent-packages/`, verifies the SHA-256
checksum and hands the swap over to a helper script (Linux: transient systemd unit `odm-agent-update`; Windows: PowerShell).
The helper stops the service, replaces the program and restarts it. If the new agent does not report back within a few minutes,
the old version is restored, and the failed version is not retried on that device (`update-failed` in the state directory).
Only an idle agent updates. An agent that no longer starts must be repaired with the install command.

## Inventory

Every `Agent:InventoryHours` (default 24) and after start the agent reports hardware (manufacturer, model, CPU, memory, architecture,
total and free system disk) and the installed programs (name, version, publisher). Windows reads the uninstall registry keys
(64 and 32 bit), Linux asks dpkg or rpm. The server keeps the latest report per device; it is shown on the device page and
in *Inventory*, where the device list for a program and version can be exported as CSV.

## Logging

The agent writes a rotating log file `agent.log` (5 × 5 MB) to `C:\ProgramData\OpenDeviceManager\logs` or
`/var/lib/odm-agent/logs` (disable with `Agent:LogToFile=false`); update runs additionally write `logs/update.log`.
On Linux the output also goes to `journalctl -u odm-agent`; on Windows warnings and above go to the Event Log
(source `OpenDeviceManagerAgent`). Log messages are English. Commands and arguments are never logged because they may contain secrets.

## Client certificates (mTLS)

In addition to its secret an agent can present a client certificate. ODM runs its own CA (ECDSA P-256). The agent creates its
key locally and sends only a certificate signing request; ODM sets the subject (`CN=<agent id>`) and key usage itself. The
certificate is issued on request and renewed through the heartbeat (30 days before expiry by default). It is bound to the
agent's credentials by its SHA-256 thumbprint.

| Setting | Meaning |
|---|---|
| `Agents:Mtls:Mode` (`ODM_MTLS_MODE`) | `Off` (default), `Optional`, `Required` |
| `Agents:Mtls:CaDirectory` | directory for `ca.crt` / `ca.key` (Docker: volume `/pki`) |
| `Agents:Mtls:CertificateValidityDays` | validity of agent certificates, default 365 |
| `Agents:Mtls:RenewBeforeDays` | renew when this many days remain, default 30 |

Requirements and behaviour:

- Either ODM terminates TLS itself (`Kestrel__Endpoints__Https__Url` and a server certificate), or a trusted reverse proxy
  forwards the certificate in a header (see below). Certificates are only issued over one of these two paths, so an agent can
  never lock itself out by enrolling over plain HTTP or through a proxy that does not forward certificates.
- Browsers may prompt for a client certificate on an endpoint that requests one; use a dedicated endpoint or port for agents.
- `Optional`: agents with a certificate must present it; agents without one are asked to request one.
  `Required`: agents without a certificate only get heartbeat and certificate issuing, no jobs.
- Recommended rollout: `Off` → `Optional` → `Required`.
- **Reset:** the device page (administrators) has *Reset certificate*, or `DELETE /api/agents/devices/{deviceId}/certificate`.
  The agent requests a new certificate with its next heartbeat.
- Back up the CA directory; if it is lost, all agents must be re-registered.

### mTLS behind a reverse proxy

The proxy terminates TLS, asks the agent for its certificate and passes it to ODM in a request header. ODM still validates the
certificate itself (issuer = agent CA, validity, key usage, subject = agent ID, stored thumbprint), so the proxy does not need
the CA. The agent's proof of key possession happens in the TLS handshake at the proxy.

| Setting | Meaning |
|---|---|
| `Proxy:ForwardedHeaders` | `true` to take client address and scheme from `X-Forwarded-For` / `-Proto` |
| `Proxy:TrustedProxies` | addresses or CIDR ranges of the proxies (`Proxy__TrustedProxies__0=172.31.250.0/24`). **Required** for forwarded certificates |
| `Agents:Mtls:ForwardedCertificateHeader` | header name, e.g. `X-Client-Cert`; empty = off |

Accepted header formats: URL-encoded PEM (nginx `$ssl_client_escaped_cert`, ingress-nginx `ssl-client-cert`), plain PEM, or
Base64 DER (Caddy, Traefik).

Security rules:

- The header is honoured **only** when the TCP peer is in `Proxy:TrustedProxies`. Keep ODM reachable only through the proxy
  (no published port, network policy), otherwise another host in the trusted range could forge the header.
- The proxy must **overwrite** the header on every request (and delete it when the client sent no certificate). The Compose
  variant does this with `header_up`.
- Do not set `ASPNETCORE_FORWARDEDHEADERS_ENABLED`; use `Proxy:ForwardedHeaders`, which keeps the original peer address for this check.

Docker Compose: `deploy/Caddyfile.mtls` (see [operations.md](operations.md#https-with-docker-compose)). Kubernetes with ingress-nginx:
set `nginx.ingress.kubernetes.io/auth-tls-pass-certificate-to-upstream: "true"`, `auth-tls-verify-client: "optional_no_ca"` and
`auth-tls-secret` (any secret with a `ca.crt`), `mtls.forwardedCertificateHeader=ssl-client-cert` and `proxy.trustedProxies` with the
ingress controller's pod range. This path is untested.
