#!/usr/bin/env bash
# Erzeugt SQL-Server-Migrationen für alle Module (Postgres-Migrationen bleiben unberührt).
# Aufruf aus dem Repository-Wurzelverzeichnis:  scripts/ef-sqlserver.sh Initial
set -euo pipefail

name="${1:?Name der Migration angeben, z. B. Initial}"

export ODM_DB_PROVIDER=SqlServer
# Zum Erzeugen von Migrationen wird keine Datenbank gebraucht; die Verbindung dient nur dem Modell.
export ODM_DB="${ODM_DB:-Server=localhost;Database=odm_design;User Id=sa;Password=unused;TrustServerCertificate=True}"

for module in Identity Devices Agents Software OsUpdates; do
  echo "== $module"
  dotnet ef migrations add "$name" \
    --context "${module}DbContext" \
    --project src/Odm.Migrations.SqlServer \
    --startup-project src/Odm.Host \
    --output-dir "$module"
done
