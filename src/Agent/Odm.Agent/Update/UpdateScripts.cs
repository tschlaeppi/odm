namespace Odm.Agent.Update;

/// <summary>
/// Helper scripts for replacing the agent's own executable. A running agent cannot replace itself;
/// the script therefore runs as a separate process, stops the service, swaps the file, restarts it and
/// restores the old version if the new agent does not report back within a few minutes.
/// </summary>
internal static class UpdateScripts
{
    public const string Linux = """
        #!/bin/sh
        # Aufruf: update.sh <Installationsordner> <Stagingordner> <Zustandsordner> <Version>
        set -u
        INSTALL="$1"; STAGE="$2"; STATE="$3"; VERSION="$4"
        LOG="$STATE/logs/update.log"
        OK="$STATE/update-ok"
        BIN="$INSTALL/odm-agent"
        log() { mkdir -p "$STATE/logs"; echo "$(date '+%F %T') $*" >> "$LOG"; }

        rm -f "$OK"
        log "Update to $VERSION started."
        systemctl stop odm-agent
        if ! cp -p "$BIN" "$BIN.bak"; then
          log "Backing up the old version failed, aborting."
          echo "$VERSION" > "$STATE/update-failed"
          systemctl start odm-agent
          exit 1
        fi
        if ! install -m 0755 "$STAGE/odm-agent" "$BIN.new" || ! mv -f "$BIN.new" "$BIN"; then
          log "Replacement failed, restoring the old version."
          mv -f "$BIN.bak" "$BIN"
          echo "$VERSION" > "$STATE/update-failed"
          systemctl start odm-agent
          exit 1
        fi
        systemctl start odm-agent

        i=0
        while [ "$i" -lt 90 ] && [ ! -f "$OK" ]; do sleep 2; i=$((i+1)); done
        if [ -f "$OK" ]; then
          rm -f "$BIN.bak"
          log "Update to $VERSION succeeded."
        else
          log "The new agent did not report back, rolling back."
          systemctl stop odm-agent
          mv -f "$BIN.bak" "$BIN"
          echo "$VERSION" > "$STATE/update-failed"
          systemctl start odm-agent
        fi
        rm -rf "$STAGE"
        """;

    public const string Windows = """
        param([string]$Install, [string]$Stage, [string]$State, [string]$Version)
        $ErrorActionPreference = 'Stop'
        $service = 'OpenDeviceManagerAgent'
        $logDir = Join-Path $State 'logs'
        $ok = Join-Path $State 'update-ok'
        $exe = Join-Path $Install 'odm-agent.exe'
        $bak = "$exe.bak"
        function Log($m) {
            New-Item -ItemType Directory -Path $logDir -Force | Out-Null
            Add-Content -Path (Join-Path $logDir 'update.log') -Value ("{0} {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $m)
        }
        function StopAgent {
            Stop-Service -Name $service -Force -ErrorAction SilentlyContinue
            (Get-Service -Name $service).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
        }

        Remove-Item $ok -ErrorAction SilentlyContinue
        Log "Update to $Version started."
        try {
            StopAgent
            Copy-Item -Path $exe -Destination $bak -Force
            Copy-Item -Path (Join-Path $Stage 'odm-agent.exe') -Destination $exe -Force
            Start-Service -Name $service
            $deadline = (Get-Date).AddSeconds(180)
            while (-not (Test-Path $ok) -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 2 }
            if (-not (Test-Path $ok)) { throw 'The new agent did not report back.' }
            Remove-Item $bak -ErrorAction SilentlyContinue
            Log "Update to $Version succeeded."
        } catch {
            Log "Rollback: $_"
            try { StopAgent } catch { }
            if (Test-Path $bak) { Copy-Item -Path $bak -Destination $exe -Force }
            Set-Content -Path (Join-Path $State 'update-failed') -Value $Version
            Start-Service -Name $service
        }
        Remove-Item $Stage -Recurse -Force -ErrorAction SilentlyContinue
        """;
}
