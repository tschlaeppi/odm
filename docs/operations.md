# Operations

## Docker Compose

```bash
cp .env.example .env          # set the values below
docker compose up -d --build
```

ODM listens on <http://localhost:8080>. The compose file starts ODM, PostgreSQL and an S3-compatible store (RustFS).
Any other S3 service works: adjust `Storage__S3__*`.

Required values in `.env`:

| Variable | Purpose |
|---|---|
| `ODM_JWT_SECRET` | signs session tokens, at least 32 characters |
| `ODM_ADMIN_USER` / `ODM_ADMIN_PASSWORD` | bootstrap administrator, created on first start when no user exists; keep it as emergency access |
| `ODM_DB_PASSWORD` | database password |
| `ODM_S3_PASSWORD` | package store password (at least 8 characters) |
| `ODM_SECRETS_KEY` | master key for stored secrets, `openssl rand -base64 32`. **Keep it safe: without it, stored secrets are unreadable.** |

Optional values: `ODM_PUBLIC_URL` (public address used in agent install scripts), `ODM_AGENT_UPDATE_*`
(see [agent.md](agent.md)), `ODM_MTLS_MODE`, `ODM_LDAP_*` and the OIDC block in `docker-compose.yml`
(see [authentication.md](authentication.md)).

## Configuration reference

Settings come from `appsettings.json` and environment variables (`Section__Key`).

| Key | Default | Meaning |
|---|---|---|
| `Database:Provider` | `Postgres` | `Postgres` or `SqlServer` |
| `Database:ConnectionString` | | connection string |
| `Jwt:Secret` / `Jwt:ExpiryMinutes` | / 60 | session token signing key (min. 32 chars) and lifetime |
| `Secrets:MasterKey` | | base64 key (32 bytes) for stored secrets |
| `Storage:S3:*` | | `ServiceUrl`, `Region`, `Bucket`, `AccessKey`, `SecretKey`, `ForcePathStyle` |
| `Identity:BootstrapAdmin:*` | | initial administrator |
| `Identity:Ldap:*`, `Identity:Oidc:*` | off | see [authentication.md](authentication.md) |
| `Software:MaxPackageMegabytes` | 4096 | upload limit for PSADT packages (S3 single upload: 5 GB maximum) |
| `Software:ReconcileSeconds` | 60 | interval of the desired/actual state reconciliation |
| `Software:RepoRefreshHours` | 24 | how often auto-updating repository packages are refreshed (0 = never) |
| `OsUpdates:CheckSeconds` | 60 | how often maintenance windows are evaluated |
| `Agents:HeartbeatSeconds` | 60 | heartbeat interval handed to agents (minimum 10) |
| `Agents:PackagesPath` | `agent-packages` | directory with the agent packages served to clients |
| `Agents:PublicBaseUrl` | | public address for install scripts (set it behind a reverse proxy) |
| `Agents:Update:*` | off | agent self-update policy, see [agent.md](agent.md) |
| `Agents:Mtls:*` | off | agent client certificates, see [agent.md](agent.md) |

## SQL Server instead of PostgreSQL

ODM runs on PostgreSQL (default) or Microsoft SQL Server 2019 or newer. Select it by configuration only:

```json
"Database": {
  "Provider": "SqlServer",
  "ConnectionString": "Server=db.example.org;Database=odm;User Id=odm;Password=…;Encrypt=True"
}
```

(Environment variables: `Database__Provider`, `Database__ConnectionString`.)

- The SQL Server migration set lives in `src/Odm.Migrations.SqlServer`; see [development.md](development.md) for how
  to create it. ODM refuses to start with the SQL Server provider if it is missing.
- ODM creates the database on first start if the account may do so (`sa`, `dbcreator`). Otherwise create an empty
  database first; the account needs `db_owner` on it.
- The driver requires TLS. With a certificate from a trusted CA `Encrypt=True` is enough; add `TrustServerCertificate=True`
  only for development with self-signed certificates.
- SQL Server compares case-insensitively by default, PostgreSQL case-sensitively. ODM stores user and target names in
  lower case, so the application behaves the same on both.
- Docker: `docker compose -f deploy/docker-compose.mssql.yml --env-file .env up -d --build` starts ODM with SQL Server 2022
  (Developer edition; production needs a licence). `ODM_DB_PASSWORD` is the `sa` password there and must satisfy SQL Server
  rules (8+ characters, upper/lower case, digit, no `;`). Use a dedicated `db_owner` account in production.
- There is no data migration between the two databases.

## TLS and reverse proxy

- Agents refuse plain HTTP by default. Terminate TLS either in front of ODM (reverse proxy) or in ODM itself
  (`Kestrel__Endpoints__Https__Url` plus certificate configuration).
- **Behind a reverse proxy:** configure forwarded headers, otherwise rate limits apply per proxy instead of per client; set
  `Agents:PublicBaseUrl`; raise the proxy upload limit to match `Software:MaxPackageMegabytes`.
- **Agent client certificates (mTLS)** need TLS terminated by ODM itself, because a proxy does not hand the client
  certificate on. See [agent.md](agent.md#client-certificates-mtls).

## Backup and restore

Back up:

1. the database (PostgreSQL dump or SQL Server backup),
2. the package bucket (S3),
3. `ODM_SECRETS_KEY` (without it stored secrets cannot be decrypted),
4. the CA directory (`odm-pki` volume, `ca.crt` / `ca.key`) when mTLS is used. If the CA is lost, all agents must be re-registered.

## Security notes

- Passwords: PBKDF2-HMAC-SHA256 (600 000 iterations, random salt); sign-in is rate limited (10 attempts per minute and IP)
  with uniform response time for unknown users.
- Agent registration: one-time token (stored as SHA-256 hash, limited validity, consumed atomically), rate limited.
- Agent operation: random 256-bit secret per agent, stored as a hash on the server; reinstalling revokes the old credentials.
- The web UI keeps its token in `sessionStorage` (per tab).
- Secrets are stored encrypted (AES-256-GCM) and never shown again; see [software-distribution.md](software-distribution.md#secrets).
