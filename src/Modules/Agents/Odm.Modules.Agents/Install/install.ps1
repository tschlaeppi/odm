<#
Open Device Manager - Agent-Installation für Windows (Convenience-Skript)

  & ([scriptblock]::Create((irm '@@SERVER@@/api/agent/install.ps1'))) -Token '<Registrierungs-Token>'

In einer PowerShell mit Administratorrechten ausführen. Erneutes Ausführen aktualisiert den Agent;
die bestehende Registrierung bleibt erhalten.
  -Token <token>          einmaliges Registrierungs-Token (nur bei Erstinstallation nötig)
  -Server <url>           Server-Adresse (Standard: der Server, von dem dieses Skript stammt)
  -AllowInsecureHttp      unverschlüsseltes http zulassen (nur Tests)
#>
param(
    [string]$Server = '@@SERVER@@',
    [string]$Token = '',
    [switch]$AllowInsecureHttp
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$packages = @{
    'win-x64'   = @{ Version = '@@VERSION_WIN_X64@@';   Sha = '@@SHA_WIN_X64@@' }
    'win-arm64' = @{ Version = '@@VERSION_WIN_ARM64@@'; Sha = '@@SHA_WIN_ARM64@@' }
}
$serviceName = 'OpenDeviceManagerAgent'
$installDir = Join-Path $env:ProgramFiles 'OpenDeviceManager'
$stateDir = Join-Path $env:ProgramData 'OpenDeviceManager'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Bitte in einer PowerShell mit Administratorrechten ausführen.'
}
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

if ($Server -notmatch '^https?://[A-Za-z0-9.\-\[\]:]+(/[A-Za-z0-9._~\-/]*)?$') { throw "Ungültige Server-Adresse: $Server" }
if ($Server.StartsWith('http://') -and -not $AllowInsecureHttp) {
    throw 'Die Server-Adresse verwendet http. Der Agent verlangt https (Test: -AllowInsecureHttp).'
}
$Server = $Server.TrimEnd('/')
if ($Token -and $Token -notmatch '^[A-Za-z0-9._~+/=\-]+$') { throw 'Das Token enthält ungültige Zeichen.' }

$arch = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
$platform = switch ($arch) { 'AMD64' { 'win-x64' } 'ARM64' { 'win-arm64' } default { throw "Nicht unterstützte Architektur: $arch" } }
$package = $packages[$platform]
if (-not $package.Sha) { throw "Der Server bietet keinen Agent für $platform an." }

$temp = Join-Path ([IO.Path]::GetTempPath()) ('odm-agent-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    Write-Host "Lade Agent $($package.Version) ($platform) von $Server ..."
    $zip = Join-Path $temp 'agent.zip'
    Invoke-WebRequest -UseBasicParsing -Uri "$Server/api/agent/download/$platform" -OutFile $zip
    $hash = (Get-FileHash -Algorithm SHA256 -Path $zip).Hash
    if ($hash -ne $package.Sha.ToUpperInvariant()) { throw 'Die Prüfsumme des Downloads stimmt nicht überein. Installation abgebrochen.' }

    $extracted = Join-Path $temp 'x'
    Expand-Archive -Path $zip -DestinationPath $extracted
    if (-not (Test-Path (Join-Path $extracted 'odm-agent.exe'))) { throw "Das Paket enthält keine Datei 'odm-agent.exe'." }

    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne 'Stopped') {
        Write-Host 'Beende laufenden Agent ...'
        Stop-Service -Name $serviceName -Force
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }

    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
    Copy-Item -Path (Join-Path $extracted '*') -Destination $installDir -Recurse -Force

    # Die (nicht geheime) Server-Adresse steht zusätzlich in der mitgelieferten appsettings.json.
    $settingsFile = Join-Path $installDir 'appsettings.json'
    if (Test-Path $settingsFile) {
        $settings = Get-Content -Raw -Path $settingsFile | ConvertFrom-Json
        $settings.Agent.ServerUrl = $Server
        [IO.File]::WriteAllText($settingsFile, ($settings | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
    }

    # Zustandsverzeichnis nur für SYSTEM und Administratoren (enthält Agent-Geheimnis und Konfiguration)
    New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
    & icacls.exe $stateDir /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Die Berechtigungen des Zustandsverzeichnisses konnten nicht gesetzt werden.' }

    $config = Join-Path $stateDir 'agent.json'
    $statePresent = Test-Path (Join-Path $stateDir 'agent-state.json')
    if ($Token -or -not (Test-Path $config)) {
        if (-not $Token -and -not $statePresent) {
            throw 'Erstinstallation: bitte -Token angeben (Administration > Registrierung).'
        }
        $agent = [ordered]@{ ServerUrl = $Server; AllowInsecureHttp = [bool]$AllowInsecureHttp }
        if ($Token) { $agent.RegistrationToken = $Token }
        $json = ([ordered]@{ Agent = $agent } | ConvertTo-Json -Depth 3)
        [IO.File]::WriteAllText($config, $json, (New-Object Text.UTF8Encoding($false)))
    }

    $exe = Join-Path $installDir 'odm-agent.exe'
    if ($service) {
        & sc.exe config $serviceName binPath= "`"$exe`"" start= auto | Out-Null
    } else {
        New-Service -Name $serviceName -BinaryPathName "`"$exe`"" -DisplayName 'Open Device Manager Agent' `
            -Description 'Verwaltet dieses Gerät über Open Device Manager.' -StartupType Automatic | Out-Null
    }
    & sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/30000/restart/60000 | Out-Null

    Start-Service -Name $serviceName
    Write-Host "Agent $($package.Version) installiert und gestartet (Dienst $serviceName)."
}
finally {
    Remove-Item -Path $temp -Recurse -Force -ErrorAction SilentlyContinue
}
