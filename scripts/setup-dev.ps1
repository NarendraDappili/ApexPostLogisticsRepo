<#
.SYNOPSIS
    Prepares a fresh clone for local development in one command.

.DESCRIPTION
    1. Restores the pinned dotnet-ef tool from dotnet-tools.json.
    2. Stores ConnectionStrings:ApexPostDb in user-secrets, only if it is not already set.
       An existing value is never overwritten or printed.
    3. Stores a demo-account password (ApexPost:DemoSeed:Password) in user-secrets, only if it is not already set.
       A new one is generated at random and printed once; no password is kept in source control.
    4. Creates ApexPostDb if needed and applies every migration (tables, stored procedures, indexes).
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

function New-DemoPassword {
    $bytes = New-Object byte[] 18
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) } finally { $generator.Dispose() }
    'Demo-' + ([Convert]::ToBase64String($bytes) -replace '[+/=]', 'x')
}

Write-Host '1/4 Restoring the pinned dotnet-ef tool'
Invoke-Checked { dotnet tool restore }

$secrets = dotnet user-secrets list --project src/ApexPost.Web

Write-Host '2/4 Connection string in user-secrets'
if ($secrets -match '^ConnectionStrings:ApexPostDb = ') {
    Write-Host '    Already set; left unchanged.'
}
else {
    Invoke-Checked { dotnet user-secrets set --project src/ApexPost.Web 'ConnectionStrings:ApexPostDb' $ConnectionString | Out-Null }
    Write-Host '    Set.'
}

Write-Host '3/4 Demo-account password in user-secrets'
if ($secrets -match '^ApexPost:DemoSeed:Password = ') {
    Write-Host '    Already set; left unchanged. See it with: dotnet user-secrets list --project src/ApexPost.Web'
}
else {
    $demoPassword = New-DemoPassword
    Invoke-Checked { dotnet user-secrets set --project src/ApexPost.Web 'ApexPost:DemoSeed:Password' $demoPassword | Out-Null }
    Write-Host "    Generated. Every demo account will use: $demoPassword"
}

Write-Host '4/4 Creating ApexPostDb and applying the migrations'
Invoke-Checked { dotnet ef database update --project src/ApexPost.Infrastructure }

Write-Host ''
Write-Host 'Ready. Start the app with:  dotnet run --project src/ApexPost.Web'
Write-Host 'The first start in Development seeds the demo data. Sign in as STF-CLERK-01-A, STF-MANAGER-01 or STF-ADMIN-01 with the demo password above.'
