#!/usr/bin/env bash
# Creates SQL Server migrations for all modules (PostgreSQL migrations stay untouched).
# Run from the repository root:  scripts/ef-sqlserver.sh Initial
set -euo pipefail

name="${1:?Name der Migration angeben, z. B. Initial}"

export ODM_DB_PROVIDER=SqlServer
# No database is needed to create migrations; the connection string only serves the model.
export ODM_DB="${ODM_DB:-Server=localhost;Database=odm_design;User Id=sa;Password=unused;TrustServerCertificate=True}"

# Restore packages (the migrations project is new and has not been restored yet)
dotnet restore src/Odm.Host

for module in Identity Devices Agents Software OsUpdates; do
  echo "== $module"
  dotnet ef migrations add "$name" \
    --context "${module}DbContext" \
    --project src/Odm.Migrations.SqlServer \
    --startup-project src/Odm.Host \
    --output-dir "$module"
done
