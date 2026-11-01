<#
.SYNOPSIS
    Prepares a fresh clone for local development in one command.

.DESCRIPTION
    1. Restores the pinned dotnet-ef tool from dotnet-tools.json.
    2. Stores ConnectionStrings:ApexPostDb in user-secrets, only if it is not already set.
       An existing value is never overwritten or printed.
    3. Creates ApexPostDb if needed and applies every migration (tables, stored procedures, indexes).
    Then run the app: in Development it seeds the demo data on the first start.

.EXAMPLE
    .\scripts\setup-dev.ps1

.EXAMPLE
    .\scripts\setup-dev.ps1 -ConnectionString "Server=(localdb)\MSSQLLocalDB;Database=ApexPostDb;Trusted_Connection=True"
#>
param(
    [string] $ConnectionString = 'Server=.;Database=ApexPostDb;Trusted_Connection=True;TrustServerCertificate=True'
)

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

function Invoke-Checked([scriptblock] $Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "Setup stopped: '$Command' failed with exit code $LASTEXITCODE." }
}

Write-Host '1/3 Restoring the pinned dotnet-ef tool'
Invoke-Checked { dotnet tool restore }

Write-Host '2/3 Connection string in user-secrets'
$secrets = dotnet user-secrets list --project src/ApexPost.Web
if ($secrets -match '^ConnectionStrings:ApexPostDb = ') {
    Write-Host '    Already set; left unchanged.'
}
else {
    Invoke-Checked { dotnet user-secrets set --project src/ApexPost.Web 'ConnectionStrings:ApexPostDb' $ConnectionString | Out-Null }
    Write-Host '    Set.'
}

Write-Host '3/3 Creating ApexPostDb and applying the migrations'
Invoke-Checked { dotnet ef database update --project src/ApexPost.Infrastructure }

Write-Host ''
Write-Host 'Ready. Start the app with:  dotnet run --project src/ApexPost.Web'
Write-Host 'The first start in Development seeds the demo data. Sign in as STF-CLERK-01-A, STF-MANAGER-01 or STF-ADMIN-01 with ApexPost-Demo-2026.'
