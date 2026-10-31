# Development

## Prerequisites

- .NET SDK 10, Node.js 22+, Docker
- `dotnet tool install --global dotnet-ef`

## Run locally

```bash
# 1. Database
docker run -d --name odm-db -p 5432:5432 -e POSTGRES_DB=odm -e POSTGRES_USER=odm -e POSTGRES_PASSWORD=odm postgres:17

# 2. Package storage (S3 compatible, here RustFS; credentials as in appsettings.Development.json)
docker run -d --name odm-rustfs -p 9000:9000 -p 9001:9001 \
  -e RUSTFS_ACCESS_KEY=minioadmin -e RUSTFS_SECRET_KEY=minioadmin \
  -v $(pwd)/data:/data rustfs/rustfs:latest

# 3. Backend: http://localhost:8080 (Development: admin / admin-dev-password, demo devices)
dotnet run --project src/Odm.Host

# 4. Frontend: http://localhost:5173, proxies /api to the backend
cd web && npm install && npm run dev
```

Migrations are applied automatically on start.

### Try the agent locally

1. Start server and frontend, sign in.
2. **Enrollment** menu: create a registration token.
3. Start an agent (plain HTTP is allowed for development only):

```bash
Agent__ServerUrl=http://localhost:8080 \
Agent__AllowInsecureHttp=true \
Agent__RegistrationToken=<token> \
Agent__StateDirectory=/tmp/odm-agent \
dotnet run --project src/Agent/Odm.Agent
```

4. The device appears in the device list. *Jobs → Send ping* creates a test job; the result arrives with the next
   heartbeat (`Agents:HeartbeatSeconds`, default 60 s, minimum 10 s).

## Build

```bash
dotnet build                      # whole solution
cd web && npm run build           # web UI (output in web/dist)
scripts/publish-agent.sh          # self-contained agent packages into agent-packages/ (publish-agent.ps1 on Windows)
docker build -t odm .             # image with UI, server and agent packages
docker build --build-arg AGENT_RIDS="" -t odm .   # faster: without agent packages
```

## Tests

```bash
dotnet test
cd web && npx svelte-check
```

`tests/Odm.Tests` covers logic that needs neither devices nor a database: maintenance windows (time zones, DST),
group rules, package settings and validation, the installation state machine (retries, timeouts, uninstall),
secrets and password hashing, agent argument handling, update planning, the agent certificate authority.
Not covered: database access, HTTP endpoints and the real package managers on devices.

## EF Core migrations

Every module has its own DB schema and its own migration history. **Migrations are provider specific**, so each change
to a data model needs two migrations:

```bash
# PostgreSQL (migrations live in the module)
dotnet ef migrations add <Name> -p src/Modules/<Module>/Odm.Modules.<Module> -o Infrastructure/Migrations

# SQL Server (migrations live in src/Odm.Migrations.SqlServer)
scripts/ef-sqlserver.sh <Name>          # Linux / macOS
.\scripts\ef-sqlserver.ps1 <Name>       # Windows
```

Modules: `Identity`, `Audit`, `Devices`, `Agents`, `Software`, `OsUpdates`. `scripts/ef-sqlserver.sh <Name> [Module ...]` limits the SQL Server migration to the given modules (e.g. `scripts/ef-sqlserver.sh Initial Audit` for a new module). Commit the generated files. The host applies the set of the
configured provider on start and aborts with a clear message if the SQL Server set is missing. If the model has pending
changes without a migration, the server does not start.

## Conventions

- **Language:** documentation, log messages and commit messages are English. The UI is localised (German / English) via
  `web/src/lib/i18n.svelte.ts`; add every new string in both languages.
- **Modules:** every module implements `IModule` and is registered in `Odm.Host/Program.cs`. Modules reference only
  `Odm.SharedKernel`, never each other; cross-module calls go through the contracts in `Odm.SharedKernel/Contracts`.
- **Documentation:** the README and `docs/` describe how things are, not project progress. Record significant decisions
  in the ADR table of [docs/architecture/arc42.md](architecture/arc42.md).
- **Security:** never log secrets, tokens or command lines that may contain secrets; never disable TLS verification.
