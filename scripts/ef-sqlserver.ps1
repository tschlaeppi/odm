# Creates SQL Server migrations for all modules (PostgreSQL migrations stay untouched).
# Run from the repository root:  .\scripts\ef-sqlserver.ps1 <Name> [Module ...]
# Without module names every module gets a migration; pass names to limit it (e.g. .\scripts\ef-sqlserver.ps1 Initial Audit).
param([Parameter(Mandatory = $true)][string]$Name, [Parameter(ValueFromRemainingArguments = $true)][string[]]$Modules)

$ErrorActionPreference = 'Stop'
$env:ODM_DB_PROVIDER = 'SqlServer'
# No database is needed to create migrations; the connection string only serves the model.
if (-not $env:ODM_DB) { $env:ODM_DB = 'Server=localhost;Database=odm_design;User Id=sa;Password=unused;TrustServerCertificate=True' }

# Restore packages (the migrations project is new and has not been restored yet)
dotnet restore src/Odm.Host
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

if (-not $Modules) { $Modules = 'Identity', 'Audit', 'Devices', 'Agents', 'Software', 'OsUpdates' }
foreach ($module in $Modules) {
    Write-Host "== $module"
    dotnet ef migrations add $Name `
        --context "${module}DbContext" `
        --project src/Odm.Migrations.SqlServer `
        --startup-project src/Odm.Host `
        --output-dir $module
    if ($LASTEXITCODE -ne 0) { throw "Migration for $module failed." }
}
