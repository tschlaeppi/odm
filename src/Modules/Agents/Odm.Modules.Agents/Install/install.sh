#!/bin/sh
# Open Device Manager - Agent-Installation für Linux (Convenience-Skript)
#
#   curl -fsSL '@@SERVER@@/api/agent/install.sh' | sudo sh -s -- --token '<Registrierungs-Token>'
#
# Erneutes Ausführen aktualisiert den Agent; die bestehende Registrierung bleibt erhalten.
# Optionen: --token <token>   einmaliges Registrierungs-Token (nur bei Erstinstallation nötig)
#           --server <url>    Server-Adresse (Standard: der Server, von dem dieses Skript stammt)
#           --allow-insecure-http   unverschlüsseltes http zulassen (nur Tests)
set -eu

SERVER='@@SERVER@@'
VERSION_LINUX_X64='@@VERSION_LINUX_X64@@'
SHA_LINUX_X64='@@SHA_LINUX_X64@@'
VERSION_LINUX_ARM64='@@VERSION_LINUX_ARM64@@'
SHA_LINUX_ARM64='@@SHA_LINUX_ARM64@@'

TOKEN=''
ALLOW_HTTP=0
INSTALL_DIR=/opt/odm-agent
STATE_DIR=/var/lib/odm-agent
UNIT=/etc/systemd/system/odm-agent.service

die() { echo "Fehler: $*" >&2; exit 1; }
need() { command -v "$1" >/dev/null 2>&1 || die "'$1' wird benötigt, ist aber nicht installiert."; }

while [ $# -gt 0 ]; do
  case "$1" in
    --token) [ $# -ge 2 ] || die "--token braucht einen Wert."; TOKEN=$2; shift 2 ;;
    --server) [ $# -ge 2 ] || die "--server braucht einen Wert."; SERVER=$2; shift 2 ;;
    --allow-insecure-http) ALLOW_HTTP=1; shift ;;
    -h|--help) sed -n '2,10p' "$0" 2>/dev/null || true; exit 0 ;;
    *) die "Unbekannte Option: $1" ;;
  esac
done

[ "$(id -u)" -eq 0 ] || die "Bitte als root ausführen (sudo)."
need uname; need tar; need sha256sum; need systemctl
if command -v curl >/dev/null 2>&1; then
  fetch() { curl -fsSL --retry 3 -o "$2" "$1"; }
elif command -v wget >/dev/null 2>&1; then
  fetch() { wget -q -O "$2" "$1"; }
else
  die "curl oder wget wird benötigt."
fi

case "$SERVER" in
  https://*) ;;
  http://*) [ "$ALLOW_HTTP" -eq 1 ] || die "Die Server-Adresse verwendet http. Der Agent verlangt https (Test: --allow-insecure-http)." ;;
  *) die "Ungültige Server-Adresse: $SERVER" ;;
esac
case "$SERVER" in *[!]A-Za-z0-9.:/_~[-]*) die "Die Server-Adresse enthält ungültige Zeichen." ;; esac
SERVER=${SERVER%/}

case "$TOKEN" in *[!A-Za-z0-9._~+/=-]*) die "Das Token enthält ungültige Zeichen." ;; esac

case "$(uname -m)" in
  x86_64|amd64) PLATFORM=linux-x64; VERSION=$VERSION_LINUX_X64; SHA=$SHA_LINUX_X64 ;;
  aarch64|arm64) PLATFORM=linux-arm64; VERSION=$VERSION_LINUX_ARM64; SHA=$SHA_LINUX_ARM64 ;;
  *) die "Nicht unterstützte Architektur: $(uname -m)" ;;
esac
[ -n "$SHA" ] || die "Der Server bietet keinen Agent für $PLATFORM an."

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT INT TERM

echo "Lade Agent $VERSION ($PLATFORM) von $SERVER ..."
fetch "$SERVER/api/agent/download/$PLATFORM" "$TMP/agent.tar.gz"
echo "$SHA  $TMP/agent.tar.gz" | sha256sum -c - >/dev/null 2>&1 \
  || die "Die Prüfsumme des Downloads stimmt nicht überein. Installation abgebrochen."

mkdir "$TMP/x"
tar -xzf "$TMP/agent.tar.gz" -C "$TMP/x"
[ -f "$TMP/x/odm-agent" ] || die "Das Paket enthält keine Datei 'odm-agent'."

if systemctl is-active --quiet odm-agent 2>/dev/null; then
  echo "Beende laufenden Agent ..."
  systemctl stop odm-agent
fi

mkdir -p "$INSTALL_DIR"
install -m 0755 "$TMP/x/odm-agent" "$INSTALL_DIR/odm-agent"
if [ -f "$TMP/x/appsettings.json" ]; then
  # Die (nicht geheime) Server-Adresse steht zusätzlich in der mitgelieferten appsettings.json.
  sed "s|\"ServerUrl\": \"\"|\"ServerUrl\": \"$SERVER\"|" "$TMP/x/appsettings.json" > "$INSTALL_DIR/appsettings.json"
  chmod 0644 "$INSTALL_DIR/appsettings.json"
fi

mkdir -p "$STATE_DIR"
chmod 0700 "$STATE_DIR"

# agent.json liegt im geschützten Zustandsverzeichnis; der Agent entfernt das Token nach der Registrierung selbst.
CONFIG="$STATE_DIR/agent.json"
if [ "$ALLOW_HTTP" -eq 1 ]; then INSECURE=true; else INSECURE=false; fi
if [ -n "$TOKEN" ]; then
  umask 077
  printf '{\n  "Agent": {\n    "ServerUrl": "%s",\n    "AllowInsecureHttp": %s,\n    "RegistrationToken": "%s"\n  }\n}\n' "$SERVER" "$INSECURE" "$TOKEN" > "$CONFIG"
elif [ ! -f "$CONFIG" ]; then
  [ -f "$STATE_DIR/agent-state.json" ] || die "Erstinstallation: bitte --token angeben (Administration > Registrierung)."
  umask 077
  printf '{\n  "Agent": {\n    "ServerUrl": "%s",\n    "AllowInsecureHttp": %s\n  }\n}\n' "$SERVER" "$INSECURE" > "$CONFIG"
fi
chmod 0600 "$CONFIG"

cat > "$UNIT" <<'UNITEOF'
[Unit]
Description=Open Device Manager Agent
After=network-online.target
Wants=network-online.target

[Service]
Type=notify
ExecStart=/opt/odm-agent/odm-agent
Restart=always
RestartSec=10
EnvironmentFile=-/etc/odm-agent/agent.env

[Install]
WantedBy=multi-user.target
UNITEOF

systemctl daemon-reload
systemctl enable odm-agent >/dev/null 2>&1
systemctl restart odm-agent

echo "Agent $VERSION installiert und gestartet. Status: systemctl status odm-agent"
