<#
.SYNOPSIS
    Runs the /sql report scripts against ApexPostDb_Perf with the actual execution plan and records, for the table
    each script filters by date, the optimizer's estimated rows against the actual rows, plus the batch's logical reads.

.DESCRIPTION
    Used for PERFORMANCE.md, "Investigation 3: local variables in the /sql report scripts". Each script runs twice;
    the first run warms the cache and the second is reported. Results are printed and written to
    <OutputDirectory>\sql-scripts-<Label>.csv. Read-only: the scripts only SELECT.

.EXAMPLE
    # The scripts as they are now
    .\database\perf\Measure-SqlScripts.ps1 -Label after

.EXAMPLE
    # The scripts before OPTION (RECOMPILE) was added, from Git
    $before = New-Item -ItemType Directory -Force (Join-Path $env:TEMP 'sql-before')
    Get-ChildItem sql -Filter '0*.sql' | ForEach-Object { git show "a15e376:sql/$($_.Name)" | Set-Content (Join-Path $before $_.Name) }
    .\database\perf\Measure-SqlScripts.ps1 -Label before -ScriptDirectory $before
#>
param(
    [Parameter(Mandatory)] [ValidatePattern('^[a-z0-9-]+$')] [string] $Label,
    [string] $ScriptDirectory = (Join-Path $PSScriptRoot '..\..\sql'),
    [string] $Server = '.',
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\..\docs\performance')
)

$ErrorActionPreference = 'Stop'

# The table whose date range each report filters.
$filteredTable = @{ '01' = 'CounterTransactions'; '02' = 'CounterTransactions'; '03' = 'CounterTransactions'; '04' = 'CounterTransactions'; '05' = 'MoneyOrders' }

$connection = New-Object System.Data.SqlClient.SqlConnection "Server=$Server;Database=ApexPostDb_Perf;Integrated Security=True;TrustServerCertificate=True"
$connection.Open()
$script:reads = 0
$connection.add_InfoMessage({ param($sender, $e) foreach ($m in [regex]::Matches($e.Message, 'logical reads (\d+)')) { $script:reads += [int]$m.Groups[1].Value } })

$scripts = Get-ChildItem $ScriptDirectory -Filter '0*_*.sql' | Where-Object Name -match '^0[1-5]_' | Sort-Object Name
$rows = foreach ($file in $scripts) {
    $table = $filteredTable[$file.Name.Substring(0, 2)]
    $sql = Get-Content $file.FullName -Raw
    foreach ($run in 1..2) {
        $script:reads = 0
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 300
        $command.CommandText = "SET STATISTICS IO ON; SET STATISTICS XML ON;`n$sql`nSET STATISTICS XML OFF; SET STATISTICS IO OFF;"
        $plans = New-Object System.Collections.Generic.List[xml]
        $reader = $command.ExecuteReader()
        do {
            if ($reader.FieldCount -eq 1 -and $reader.GetName(0) -like '*Showplan*') { while ($reader.Read()) { $plans.Add([xml]$reader.GetString(0)) } }
            else { while ($reader.Read()) { } }
        } while ($reader.NextResult())
        $reader.Close()
    }

    $statement = 0
    foreach ($plan in $plans) {
        $statement++
        $ns = New-Object Xml.XmlNamespaceManager $plan.NameTable
        $ns.AddNamespace('p', 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
        foreach ($op in $plan.SelectNodes("//p:RelOp[p:IndexScan/p:Object[@Table='[$table]']]", $ns)) {
            $actual = ($op.SelectNodes('p:RunTimeInformation/p:RunTimeCountersPerThread', $ns) | ForEach-Object { [long]$_.ActualRows } | Measure-Object -Sum).Sum
            # Total estimate over all executions of the operator, comparable with the summed actual rows.
            $estimated = [double]$op.EstimateRows * (1 + [double]$op.EstimateRebinds + [double]$op.EstimateRewinds)
            [pscustomobject]@{
                Run = $Label; Script = $file.Name; Stmt = $statement; Operator = $op.PhysicalOp
                Index = $op.SelectSingleNode('p:IndexScan/p:Object', $ns).Index
                Estimated = [math]::Round($estimated, 1); Actual = $actual; BatchReads = $script:reads
            }
        }
    }
}
$connection.Close()

$rows | Format-Table -AutoSize | Out-String -Width 250
$rows | Export-Csv -NoTypeInformation -Path (Join-Path $OutputDirectory "sql-scripts-$Label.csv")
