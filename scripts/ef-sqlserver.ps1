# Erzeugt SQL-Server-Migrationen für alle Module (Postgres-Migrationen bleiben unberührt).
# Aufruf aus dem Repository-Wurzelverzeichnis:  .\scripts\ef-sqlserver.ps1 Initial
param([Parameter(Mandatory = $true)][string]$Name)

$ErrorActionPreference = 'Stop'
$env:ODM_DB_PROVIDER = 'SqlServer'
# Zum Erzeugen von Migrationen wird keine Datenbank gebraucht; die Verbindung dient nur dem Modell.
if (-not $env:ODM_DB) { $env:ODM_DB = 'Server=localhost;Database=odm_design;User Id=sa;Password=unused;TrustServerCertificate=True' }

foreach ($module in 'Identity', 'Devices', 'Agents', 'Software', 'OsUpdates') {
    Write-Host "== $module"
    dotnet ef migrations add $Name `
        --context "${module}DbContext" `
        --project src/Odm.Migrations.SqlServer `
        --startup-project src/Odm.Host `
        --output-dir $module
    if ($LASTEXITCODE -ne 0) { throw "Migration für $module fehlgeschlagen." }
}
