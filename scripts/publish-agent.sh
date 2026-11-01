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
  # Licence texts travel with every package: ODM (Apache-2.0) and the bundled .NET runtime (MIT, third-party notices).
  cp LICENSE NOTICE "$dir/"
  # The runtime pack is where "dotnet publish -r" got it from: the NuGet cache, or the packs folder of the SDK.
  dotnet_root="${DOTNET_ROOT:-$(dirname "$(readlink -f "$(command -v dotnet)")")}"
  found=0
  for root in "${NUGET_PACKAGES:-$HOME/.nuget/packages}/microsoft.netcore.app.runtime.$rid" "$dotnet_root/packs/Microsoft.NETCore.App.Runtime.$rid"; do
    runtime_pack=$(ls -d "$root"/*/ 2>/dev/null | sort -V | tail -n 1 || true)
    if [ -n "$runtime_pack" ] && [ -f "${runtime_pack}LICENSE.txt" ] && [ -f "${runtime_pack}THIRD-PARTY-NOTICES.TXT" ]; then
      cp "${runtime_pack}LICENSE.txt" "$dir/dotnet-LICENSE.txt"
      cp "${runtime_pack}THIRD-PARTY-NOTICES.TXT" "$dir/dotnet-THIRD-PARTY-NOTICES.txt"
      found=1
      break
    fi
  done
  if [ "$found" = 0 ]; then
    echo "WARNING: licence files of the .NET runtime pack not found for $rid (searched the NuGet cache and $dotnet_root/packs); the package lacks dotnet-LICENSE.txt and dotnet-THIRD-PARTY-NOTICES.txt. Set NUGET_PACKAGES or DOTNET_ROOT if your folders are elsewhere." >&2
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
echo "Pakete in $OUT"
