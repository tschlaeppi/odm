# Builds the self-contained, single-file agent packages for download through the server.
#   scripts\publish-agent.ps1 [-Out agent-packages] [-Rids win-x64,linux-x64,...]
param(
    [string]$Out = 'agent-packages',
    [string[]]$Rids = @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64')
)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$project = 'src/Agent/Odm.Agent/Odm.Agent.csproj'
$version = (& dotnet msbuild $project -getProperty:Version -nologo).Trim()
if (-not $version) { throw 'Could not determine the version.' }

New-Item -ItemType Directory -Path $Out -Force | Out-Null
$Out = (Resolve-Path $Out).Path
$work = Join-Path ([IO.Path]::GetTempPath()) ('odm-publish-' + [Guid]::NewGuid().ToString('N'))
try {
    foreach ($rid in $Rids) {
        $dir = Join-Path $work $rid
        Write-Host "==> $rid ($version)"
        & dotnet publish $project -c Release -r $rid --self-contained true `
            -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o $dir
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish ($rid) failed." }
        # Licence texts travel with every package: ODM (Apache-2.0) and the bundled .NET runtime (MIT, third-party notices).
        Copy-Item LICENSE, NOTICE -Destination $dir
        # The runtime pack is where "dotnet publish -r" got it from: the NuGet cache, or the packs folder of the SDK.
        $dotnetRoot = if ($env:DOTNET_ROOT) { $env:DOTNET_ROOT } else { Split-Path (Get-Command dotnet).Source }
        $nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget/packages' }
        $searched = @(
            Join-Path $nugetRoot "microsoft.netcore.app.runtime.$rid"
            Join-Path $dotnetRoot "packs/Microsoft.NETCore.App.Runtime.$rid"
        )
        $found = $false
        foreach ($root in $searched) {
            $pack = Get-ChildItem $root -Directory -ErrorAction SilentlyContinue |
                Where-Object { (Test-Path (Join-Path $_.FullName 'LICENSE.txt')) -and (Test-Path (Join-Path $_.FullName 'THIRD-PARTY-NOTICES.TXT')) } |
                Sort-Object { [version]($_.Name -replace '-.*$', '') } | Select-Object -Last 1
            if ($pack) {
                Copy-Item (Join-Path $pack.FullName 'LICENSE.txt') (Join-Path $dir 'dotnet-LICENSE.txt')
                Copy-Item (Join-Path $pack.FullName 'THIRD-PARTY-NOTICES.TXT') (Join-Path $dir 'dotnet-THIRD-PARTY-NOTICES.txt')
                $found = $true
                break
            }
        }
        if (-not $found) {
            Write-Warning "Licence files of the .NET runtime pack not found for $rid (searched: $($searched -join '; ')); the package lacks dotnet-*.txt. Set NUGET_PACKAGES or DOTNET_ROOT if your folders are elsewhere."
        }
        $extras = @('LICENSE', 'NOTICE') + @(Get-ChildItem $dir -Filter 'dotnet-*.txt' | ForEach-Object Name)
        if ($rid.StartsWith('win')) {
            $target = Join-Path $Out "odm-agent-$version-$rid.zip"
            Remove-Item $target -ErrorAction SilentlyContinue
            Compress-Archive -Path (@('odm-agent.exe', 'appsettings.json') + $extras | ForEach-Object { Join-Path $dir $_ }) -DestinationPath $target
        } else {
            & tar.exe -czf (Join-Path $Out "odm-agent-$version-$rid.tar.gz") -C $dir (@('odm-agent', 'appsettings.json') + $extras)
            if ($LASTEXITCODE -ne 0) { throw 'tar failed.' }
        }
    }
    Write-Host "Packages in $Out"
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
