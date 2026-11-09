<#
.SYNOPSIS
    Writes the API's OpenAPI documents to contracts/v1.json and contracts/v2.json.

.DESCRIPTION
    Builds ApexPost.Api, starts it in Development on a local port, downloads /openapi/v1.json and /openapi/v2.json
    and stops it. The documents are built from the controllers' metadata, so the database is never opened: the
    connection string is a placeholder.

    Line breaks inside descriptions (from multi-line XML comments) are written as \n, so the files are the same
    whichever operating system checked out the sources.

    Run it after any change to the API's contract, then review the diff of contracts/*.json: that diff is the
    contract change (VERSIONING.md). Regenerate the client afterwards: dotnet nswag run src/ApexPost.Client/nswag.json

    Works in Windows PowerShell 5.1 and PowerShell 7 (pwsh) on Windows, Linux and macOS.

.EXAMPLE
    ./contracts/export-contracts.ps1
#>
param(
    [int] $Port = 5299
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$api = Join-Path $root 'src/ApexPost.Api'

dotnet build $api --configuration Release --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "The API did not build." }

$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = 'dotnet'
$start.Arguments = 'bin/Release/net10.0/ApexPost.Api.dll'
$start.WorkingDirectory = $api
$start.UseShellExecute = $false
$start.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = 'Development'
$start.EnvironmentVariables['ASPNETCORE_URLS'] = "http://127.0.0.1:$Port"
$start.EnvironmentVariables['ConnectionStrings__ApexPostDb'] = 'Server=(placeholder);Database=ApexPostDb_ContractExport;Trusted_Connection=True'
$start.EnvironmentVariables['Logging__LogLevel__Default'] = 'Warning'
$process = [System.Diagnostics.Process]::Start($start)

try {
    $base = "http://127.0.0.1:$Port"
    $ready = $false
    for ($attempt = 0; $attempt -lt 60 -and -not $ready; $attempt++) {
        try { Invoke-WebRequest "$base/health" -UseBasicParsing | Out-Null; $ready = $true }
        catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw "The API did not start on $base." }

    foreach ($version in 'v1', 'v2') {
        $document = (Invoke-WebRequest "$base/openapi/$version.json" -UseBasicParsing).Content
        $document = ($document -replace '\\r\\n', '\n') -replace "`r`n", "`n"
        $path = Join-Path $PSScriptRoot "$version.json"
        [System.IO.File]::WriteAllText($path, $document.TrimEnd() + "`n", (New-Object System.Text.UTF8Encoding $false))
        Write-Host "Wrote $path"
    }
}
finally {
    if (-not $process.HasExited) { $process.Kill() }
    $process.Dispose()
}
