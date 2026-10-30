# Baut die eigenständigen Agent-Pakete (self-contained, Single-File) für den Download über den Server.
#   scripts\publish-agent.ps1 [-Out agent-packages] [-Rids win-x64,linux-x64,...]
param(
    [string]$Out = 'agent-packages',
    [string[]]$Rids = @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64')
)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$project = 'src/Agent/Odm.Agent/Odm.Agent.csproj'
$version = (& dotnet msbuild $project -getProperty:Version -nologo).Trim()
if (-not $version) { throw 'Version konnte nicht ermittelt werden.' }

New-Item -ItemType Directory -Path $Out -Force | Out-Null
$Out = (Resolve-Path $Out).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ('odm-publish-' + [Guid]::NewGuid().ToString('N'))
try {
    foreach ($rid in $Rids) {
        $dir = Join-Path $work $rid
        Write-Host "==> $rid ($version)"
        & dotnet publish $project -c Release -r $rid --self-contained true `
            -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o $dir
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish ($rid) fehlgeschlagen." }
        if ($rid.StartsWith('win')) {
            $target = Join-Path $Out "odm-agent-$version-$rid.zip"
            Remove-Item $target -ErrorAction SilentlyContinue
            Compress-Archive -Path (Join-Path $dir 'odm-agent.exe'), (Join-Path $dir 'appsettings.json') -DestinationPath $target
        } else {
            & tar.exe -czf (Join-Path $Out "odm-agent-$version-$rid.tar.gz") -C $dir odm-agent appsettings.json
            if ($LASTEXITCODE -ne 0) { throw 'tar fehlgeschlagen.' }
        }
    }
    Write-Host "Pakete in $Out"
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
