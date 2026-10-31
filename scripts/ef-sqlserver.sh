#!/usr/bin/env bash
# Creates SQL Server migrations for all modules (PostgreSQL migrations stay untouched).
# Run from the repository root:  scripts/ef-sqlserver.sh <Name> [Module ...]
# Without module names every module gets a migration; pass names to limit it (e.g. a new module: scripts/ef-sqlserver.sh Initial Audit).
set -euo pipefail

name="${1:?Give the migration name, e.g. Initial}"
shift
modules=("$@")
if [ ${#modules[@]} -eq 0 ]; then modules=(Identity Audit Notifications Devices Agents Software OsUpdates); fi

export ODM_DB_PROVIDER=SqlServer
# No database is needed to create migrations; the connection string only serves the model.
export ODM_DB="${ODM_DB:-Server=localhost;Database=odm_design;User Id=sa;Password=unused;TrustServerCertificate=True}"

# Restore packages (the migrations project is new and has not been restored yet)
dotnet restore src/Odm.Host

for module in "${modules[@]}"; do
  echo "== $module"
  dotnet ef migrations add "$name" \
    --context "${module}DbContext" \
    --project src/Odm.Migrations.SqlServer \
    --startup-project src/Odm.Host \
    --output-dir "$module"
done
