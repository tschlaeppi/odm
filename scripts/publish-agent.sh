#!/usr/bin/env bash
# Baut die eigenständigen Agent-Pakete (self-contained, Single-File) für den Download über den Server.
#   scripts/publish-agent.sh [Ausgabeverzeichnis] [RIDs...]
# Standard: ./agent-packages und alle vier Plattformen. Ergebnis: odm-agent-<version>-<rid>.zip|tar.gz
set -euo pipefail

cd "$(dirname "$0")/.."
OUT=${1:-agent-packages}
shift || true
RIDS=("$@")
[ ${#RIDS[@]} -gt 0 ] || RIDS=(win-x64 win-arm64 linux-x64 linux-arm64)

PROJECT=src/Agent/Odm.Agent/Odm.Agent.csproj
VERSION=$(dotnet msbuild "$PROJECT" -getProperty:Version -nologo | tr -d '[:space:]')
[ -n "$VERSION" ] || { echo "Version konnte nicht ermittelt werden." >&2; exit 1; }

mkdir -p "$OUT"
OUT=$(cd "$OUT" && pwd)
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

for rid in "${RIDS[@]}"; do
  dir="$WORK/$rid"
  echo "==> $rid ($VERSION)"
  dotnet publish "$PROJECT" -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o "$dir"
  case "$rid" in
    win-*)
      command -v zip >/dev/null || { echo "'zip' wird benötigt." >&2; exit 1; }
      (cd "$dir" && rm -f "$OUT/odm-agent-$VERSION-$rid.zip" && zip -q -r "$OUT/odm-agent-$VERSION-$rid.zip" odm-agent.exe appsettings.json)
      ;;
    *)
      tar -czf "$OUT/odm-agent-$VERSION-$rid.tar.gz" -C "$dir" odm-agent appsettings.json
      ;;
  esac
done

(cd "$OUT" && sha256sum odm-agent-"$VERSION"-* > "odm-agent-$VERSION.sha256")
echo "Pakete in $OUT"
