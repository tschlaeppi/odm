#!/bin/sh
# Open Device Manager - agent installation for Linux (convenience script)
#
#   curl -fsSL '@@SERVER@@/api/agent/install.sh' | sudo sh -s -- --token '<registration-token>'
#
# Running it again updates the agent; the existing registration is kept.
# Optionen: --token <token>   one-time registration token (only needed for the first installation)
#           --server <url>    server address (default: the server this script comes from)
#           --allow-insecure-http   allow unencrypted http (tests only)
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
need() { command -v "$1" >/dev/null 2>&1 || die "'$1' is required but not installed."; }

while [ $# -gt 0 ]; do
  case "$1" in
    --token) [ $# -ge 2 ] || die "--token needs a value."; TOKEN=$2; shift 2 ;;
    --server) [ $# -ge 2 ] || die "--server needs a value."; SERVER=$2; shift 2 ;;
    --allow-insecure-http) ALLOW_HTTP=1; shift ;;
    -h|--help) sed -n '2,10p' "$0" 2>/dev/null || true; exit 0 ;;
    *) die "Unknown option: $1" ;;
  esac
done

[ "$(id -u)" -eq 0 ] || die "Please run as root (sudo)."
need uname; need tar; need sha256sum; need systemctl
if command -v curl >/dev/null 2>&1; then
  fetch() { curl -fsSL --retry 3 -o "$2" "$1"; }
elif command -v wget >/dev/null 2>&1; then
  fetch() { wget -q -O "$2" "$1"; }
else
  die "curl or wget is required."
fi

case "$SERVER" in
  https://*) ;;
  http://*) [ "$ALLOW_HTTP" -eq 1 ] || die "The server address uses http. The agent requires https (for tests: --allow-insecure-http)." ;;
  *) die "Invalid server address: $SERVER" ;;
esac
case "$SERVER" in *[!]A-Za-z0-9.:/_~[-]*) die "The server address contains invalid characters." ;; esac
SERVER=${SERVER%/}

case "$TOKEN" in *[!A-Za-z0-9._~+/=-]*) die "The token contains invalid characters." ;; esac

case "$(uname -m)" in
  x86_64|amd64) PLATFORM=linux-x64; VERSION=$VERSION_LINUX_X64; SHA=$SHA_LINUX_X64 ;;
  aarch64|arm64) PLATFORM=linux-arm64; VERSION=$VERSION_LINUX_ARM64; SHA=$SHA_LINUX_ARM64 ;;
  *) die "Unsupported architecture: $(uname -m)" ;;
esac
[ -n "$SHA" ] || die "The server offers no agent for $PLATFORM an."

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT INT TERM

echo "Lade Agent $VERSION ($PLATFORM) von $SERVER ..."
fetch "$SERVER/api/agent/download/$PLATFORM" "$TMP/agent.tar.gz"
echo "$SHA  $TMP/agent.tar.gz" | sha256sum -c - >/dev/null 2>&1 \
  || die "The checksum of the download does not match. Installation aborted."

mkdir "$TMP/x"
tar -xzf "$TMP/agent.tar.gz" -C "$TMP/x"
[ -f "$TMP/x/odm-agent" ] || die "The package does not contain a file 'odm-agent'."

if systemctl is-active --quiet odm-agent 2>/dev/null; then
  echo "Beende laufenden Agent ..."
  systemctl stop odm-agent
fi

mkdir -p "$INSTALL_DIR"
install -m 0755 "$TMP/x/odm-agent" "$INSTALL_DIR/odm-agent"
if [ -f "$TMP/x/appsettings.json" ]; then
  # The (non-secret) server address is also in the bundled appsettings.json.
  sed "s|\"ServerUrl\": \"\"|\"ServerUrl\": \"$SERVER\"|" "$TMP/x/appsettings.json" > "$INSTALL_DIR/appsettings.json"
  chmod 0644 "$INSTALL_DIR/appsettings.json"
fi

mkdir -p "$STATE_DIR"
chmod 0700 "$STATE_DIR"

# agent.json lives in the protected state directory; the agent removes the token itself after registration.
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
