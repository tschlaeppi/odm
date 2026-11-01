#!/usr/bin/env bash
# Builds the self-contained, single-file agent packages for download through the server.
#   scripts/publish-agent.sh [Ausgabeverzeichnis] [RIDs...]
# Default: ./agent-packages and all four platforms. Result: odm-agent-<version>-<rid>.zip|tar.gz
set -euo pipefail

cd "$(dirname "$0")/.."
OUT=${1:-agent-packages}
shift || true
RIDS=("$@")
[ ${#RIDS[@]} -gt 0 ] || RIDS=(win-x64 win-arm64 linux-x64 linux-arm64)

PROJECT=src/Agent/Odm.Agent/Odm.Agent.csproj
VERSION=$(dotnet msbuild "$PROJECT" -getProperty:Version -nologo | tr -d '[:space:]')
[ -n "$VERSION" ] || { echo "Could not determine the version." >&2; exit 1; }

mkdir -p "$OUT"
OUT=$(cd "$OUT" && pwd)
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

for rid in "${RIDS[@]}"; do
  dir="$WORK/$rid"
  echo "==> $rid ($VERSION)"
  dotnet publish "$PROJECT" -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o "$dir"
  # Licence texts travel with every package: ODM (Apache-2.0) and the bundled .NET runtime (MIT, third-party notices).
  cp LICENSE NOTICE "$dir/"
  runtime_pack=$(ls -d "${NUGET_PACKAGES:-$HOME/.nuget/packages}/microsoft.netcore.app.runtime.$rid"/*/ 2>/dev/null | sort -V | tail -n 1 || true)
  if [ -n "$runtime_pack" ] && [ -f "${runtime_pack}LICENSE.txt" ] && [ -f "${runtime_pack}THIRD-PARTY-NOTICES.TXT" ]; then
    cp "${runtime_pack}LICENSE.txt" "$dir/dotnet-LICENSE.txt"
    cp "${runtime_pack}THIRD-PARTY-NOTICES.TXT" "$dir/dotnet-THIRD-PARTY-NOTICES.txt"
  else
    echo "WARNING: licence files of the .NET runtime pack not found for $rid; the package lacks dotnet-LICENSE.txt and dotnet-THIRD-PARTY-NOTICES.txt." >&2
  fi
  extras=$(cd "$dir" && ls LICENSE NOTICE dotnet-*.txt 2>/dev/null | tr '\n' ' ' || true)
  case "$rid" in
    win-*)
      command -v zip >/dev/null || { echo "'zip' is required." >&2; exit 1; }
      (cd "$dir" && rm -f "$OUT/odm-agent-$VERSION-$rid.zip" && zip -q -r "$OUT/odm-agent-$VERSION-$rid.zip" odm-agent.exe appsettings.json $extras)
      ;;
    *)
      tar -czf "$OUT/odm-agent-$VERSION-$rid.tar.gz" -C "$dir" odm-agent appsettings.json $extras
      ;;
  esac
done

(cd "$OUT" && sha256sum odm-agent-"$VERSION"-* > "odm-agent-$VERSION.sha256")
echo "Packages in $OUT"
