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
| `Proxy:ForwardedHeaders` | `false` | take client address and scheme from `X-Forwarded-*` (reverse proxy) |
| `Proxy:TrustedProxies` | empty | addresses / CIDR ranges of trusted proxies; empty = every peer for `X-Forwarded-*`, none for forwarded certificates |

## Kubernetes (Helm)

The chart lives in `deploy/helm/odm`. It deploys ODM as a single instance (background services and the agent CA volume
require exactly one replica), a Service, an optional Ingress, a PVC for the agent CA and, optionally, a bundled
single-node PostgreSQL. The package store (S3) is always external (AWS S3, MinIO, RustFS, ...).

```bash
docker build -t registry.example.org/odm:1.0 . && docker push registry.example.org/odm:1.0

helm install odm deploy/helm/odm -n odm --create-namespace \
  --set image.repository=registry.example.org/odm --set image.tag=1.0 \
  --set publicUrl=https://odm.example.org \
  --set ingress.enabled=true --set ingress.host=odm.example.org --set ingress.tls.secretName=odm-tls \
  --set secrets.jwtSecret="$(openssl rand -base64 48)" \
  --set secrets.secretsKey="$(openssl rand -base64 32)" \
  --set secrets.adminPassword='...' \
  --set database.connectionString='Host=pg.example.org;Database=odm;Username=odm;Password=...' \
  --set s3.serviceUrl=https://s3.example.org --set s3.accessKey=odm --set s3.secretKey='...'
```

- **Secrets:** pass them as values (the chart creates a Secret) or set `existingSecret` to a Secret you manage
  (keys: `jwtSecret`, `secretsKey`, `adminPassword`, `databaseConnectionString`, `s3SecretKey`, optional `ldapBindPassword`,
  `oidcClientSecret`). Do not keep real secrets in a values file in Git.
- **Database:** set `database.connectionString` (and `database.provider` for SQL Server), or `postgresql.enabled=true` with
  `postgresql.password` for the bundled instance (evaluation and small setups only).
- **Ingress:** the default annotations target ingress-nginx and raise the upload limit; adjust `proxy-body-size` to
  `software.maxPackageMegabytes`. With the ingress enabled the chart switches on ASP.NET Core forwarded headers
  (`Proxy__ForwardedHeaders`) so client address and scheme are taken from the proxy; restrict it with `proxy.trustedProxies`.
- **Other settings:** LDAP has its own values block; everything else (OIDC, Kestrel HTTPS, ...) goes through `extraEnv` using
  the keys of the [configuration reference](#configuration-reference).
- **mTLS** needs TLS terminated by ODM itself or an ingress that forwards the client certificate
  (`mtls.forwardedCertificateHeader`, `proxy.trustedProxies`; see [agent.md](agent.md#mtls-behind-a-reverse-proxy), untested on Kubernetes).
  Otherwise keep `mtls.mode=Off`.
- **Upgrades:** `helm upgrade` replaces the pod (strategy `Recreate`); migrations run at startup.

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

### HTTPS with Docker Compose

```bash
# .env: ODM_DOMAIN=odm.example.org   (optional: ODM_CADDY_TLS, see below)
docker compose -f docker-compose.yml -f deploy/docker-compose.https.yml up -d --build
```

This adds a [Caddy](https://caddyserver.com) reverse proxy (`deploy/Caddyfile`) on ports 80/443 and removes the direct
port 8080. It needs Docker Compose 2.24 or newer. Certificates (`ODM_CADDY_TLS`):

| Value | Result |
|---|---|
| empty | certificate from Let's Encrypt, renewed automatically; needs a public DNS name and reachable ports 80 and 443 |
| `internal` | Caddy's own CA for private networks; browsers and agents must trust its root certificate (`caddy-data` volume, `pki/authorities/local/root.crt`) |
| `/certs/fullchain.pem /certs/key.pem` | your own certificate, placed in `./certs` |

The variant sets the forwarded headers (trusted: only the proxy network `ODM_PROXY_SUBNET`), `Agents__PublicBaseUrl` and the
upload limit (4 GB, as `Software:MaxPackageMegabytes`; change both together). **Agent client certificates (mTLS):** set
`ODM_PROXY_CADDYFILE=./deploy/Caddyfile.mtls` and `ODM_MTLS_MODE=Optional` (or `Required`); Caddy then forwards the agent
certificate to ODM in the `X-Client-Cert` header, see [agent.md](agent.md#mtls-behind-a-reverse-proxy).

### General notes

- Agents refuse plain HTTP by default. Terminate TLS either in front of ODM (reverse proxy) or in ODM itself
  (`Kestrel__Endpoints__Https__Url` plus certificate configuration).
- **Behind a reverse proxy:** set `Proxy:ForwardedHeaders=true` (and `Proxy:TrustedProxies` to the proxy addresses), otherwise
  rate limits apply per proxy instead of per client; set `Agents:PublicBaseUrl`; raise the proxy upload limit to match
  `Software:MaxPackageMegabytes`.
- **Agent client certificates (mTLS)** need either TLS terminated by ODM itself or a trusted proxy that forwards the
  certificate in a header. See [agent.md](agent.md#client-certificates-mtls).

## Backup and restore

Back up:

1. the database (PostgreSQL dump or SQL Server backup),
2. the package bucket (S3),
3. `ODM_SECRETS_KEY` (without it stored secrets cannot be decrypted),
4. the CA directory (`odm-pki` volume / Kubernetes PVC `<release>-odm-pki`, `ca.crt` / `ca.key`) when mTLS is used. If the CA is lost, all agents must be re-registered.

## Security notes

- Passwords: PBKDF2-HMAC-SHA256 (600 000 iterations, random salt); sign-in is rate limited (10 attempts per minute and IP)
  with uniform response time for unknown users.
- Agent registration: one-time token (stored as SHA-256 hash, limited validity, consumed atomically), rate limited.
- Agent operation: random 256-bit secret per agent, stored as a hash on the server; reinstalling revokes the old credentials.
- The web UI keeps its token in `sessionStorage` (per tab).
- Secrets are stored encrypted (AES-256-GCM) and never shown again; see [software-distribution.md](software-distribution.md#secrets).
