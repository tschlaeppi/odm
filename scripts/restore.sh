#!/usr/bin/env bash
# Restores a backup made by scripts/backup.sh into a docker-compose.yml installation (PostgreSQL).
# Run from the repository root:  scripts/restore.sh <backup-directory>
# The database content is replaced. ODM_SECRETS_KEY in .env must be the key used when the backup was made.
set -euo pipefail

dir="${1:?Give the backup directory, e.g. backup/20261031-120000}"
[ -f "$dir/odm.dump" ] || { echo "$dir/odm.dump not found." >&2; exit 1; }

read -r -p "This replaces the current ODM database. Continue? [y/N] " answer
[ "$answer" = "y" ] || exit 1

docker compose up -d db
docker compose stop odm

echo "Database ..."
docker compose exec -T db pg_restore -U odm -d odm --clean --if-exists --no-owner < "$dir/odm.dump"

if [ -d "$dir/pki" ]; then
  echo "Agent CA ..."
  docker compose cp "$dir/pki/." odm:/pki
  # 1654 = the "app" user of the .NET images; the CA key must belong to it
  docker compose run --rm --no-deps -u root --entrypoint chown odm -R 1654 /pki
fi

if [ -d "$dir/packages" ]; then
  echo "Package store ..."
  docker compose cp "$dir/packages/." s3:/data
fi

docker compose up -d
echo "Restore finished."
