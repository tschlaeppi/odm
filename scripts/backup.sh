#!/usr/bin/env bash
# Backup of an ODM installation started with the repository's docker-compose.yml (PostgreSQL).
# Run from the repository root:  scripts/backup.sh [target-directory] [--with-packages]
#
# Saves: database dump (odm.dump), agent CA (pki/), optionally the package store of the bundled S3 service.
# NOT saved: ODM_SECRETS_KEY (keep it separately and safely - without it stored secrets are unreadable) and .env.
# For an external S3 service back up the bucket with that service's tools (e.g. "mc mirror").
set -euo pipefail

target=""
packages=0
for arg in "$@"; do
  case "$arg" in
    --with-packages) packages=1 ;;
    *) target="$arg" ;;
  esac
done
target="${target:-backup/$(date +%Y%m%d-%H%M%S)}"
mkdir -p "$target"

echo "Database ..."
docker compose exec -T db pg_dump -U odm -d odm -Fc > "$target/odm.dump"

echo "Agent CA ..."
docker compose cp odm:/pki "$target/pki" || echo "  (no CA directory found - mTLS not in use?)"

if [ "$packages" -eq 1 ]; then
  echo "Package store ..."
  docker compose cp s3:/data "$target/packages"
fi

chmod -R go-rwx "$target"   # contains the CA private key
echo "Backup written to $target"
echo "Reminder: ODM_SECRETS_KEY is not part of the backup."
