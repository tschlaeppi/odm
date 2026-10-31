# Open Device Manager (ODM)

A free alternative to Microsoft Configuration Manager / Intune: software distribution and lifecycle,
OS update management and device inventory for Windows and Linux fleets.

- **Server:** C# / .NET 10 modular monolith, PostgreSQL or SQL Server, S3-compatible package storage
- **Web UI:** Svelte 5 + TypeScript (German / English, dark mode)
- **Agent:** self-contained .NET worker service for Windows and Linux (DEB and RPM based)
- **Deployment:** Docker / Docker Compose, Kubernetes (Helm chart)

The product requirements are in [REQUIREMENTS.md](REQUIREMENTS.md).

## Quick start (Docker Compose)

```bash
cp .env.example .env          # set the secrets, see docs/operations.md
docker compose up -d --build
```

ODM is then available on <http://localhost:8080>. The administrator is created on first start from
`ODM_ADMIN_USER` / `ODM_ADMIN_PASSWORD`. Use HTTPS in production; agents refuse plain HTTP by default.

## Quick start (development)

```bash
docker run -d --name odm-db -p 5432:5432 -e POSTGRES_DB=odm -e POSTGRES_USER=odm -e POSTGRES_PASSWORD=odm postgres:17
dotnet run --project src/Odm.Host          # http://localhost:8080 (admin / admin-dev-password)
cd web && npm install && npm run dev       # http://localhost:5173
```

Details, S3 storage, migrations and tests: [docs/development.md](docs/development.md).

## Documentation

| Document | Audience | Content |
|---|---|---|
| [docs/development.md](docs/development.md) | developers | prerequisites, build, run, tests, EF migrations, conventions |
| [docs/operations.md](docs/operations.md) | operators | Docker Compose, SQL Server, configuration reference, TLS / reverse proxy, backup |
| [docs/authentication.md](docs/authentication.md) | operators | local accounts, LDAP / Active Directory, OpenID Connect |
| [docs/agent.md](docs/agent.md) | operators | agent installation, self-update, logging, mTLS |
| [docs/software-distribution.md](docs/software-distribution.md) | operators | PSADT and repository packages, secrets, profiles, rollouts, uninstall |
| [docs/os-updates.md](docs/os-updates.md) | operators | update policies, maintenance windows, how the agent updates |
| [docs/architecture/arc42.md](docs/architecture/arc42.md) | everyone | architecture following arc42, including the decision log (ADR table) |

## Repository layout

```
src/Odm.SharedKernel/   shared base: IModule, Entity, database configuration, cross-module contracts
src/Odm.Host/           ASP.NET Core host: wires all modules, serves the web UI
src/Modules/            Identity, Audit, Devices, Agents, Software, OsUpdates
src/Agent/Odm.Agent/    the agent (worker service)
src/Odm.Migrations.SqlServer/   SQL Server migration set
web/                    Svelte web UI
tests/Odm.Tests/        unit tests
deploy/                 systemd unit, SQL Server and HTTPS compose files, Helm chart
scripts/                agent packaging, SQL Server migration helper
```
