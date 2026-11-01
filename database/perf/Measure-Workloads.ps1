<#
.SYNOPSIS
    Measures the PERFORMANCE.md workloads against ApexPostDb_Perf: logical reads (SET STATISTICS IO), CPU and elapsed
    time (SET STATISTICS TIME), and the actual execution plan (SET STATISTICS XML), saved as a .sqlplan file.

.DESCRIPTION
    Every workload is sent exactly as the application sends it: the EF Core LINQ queries as parameterised
    sp_executesql batches (the SQL captured by GeneratedSqlTests with ToQueryString), the procedures as parameterised
    EXEC calls, with the same parameter types (varchar(48), date, int, datetime2).

    Method, per workload:
      1. ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE, so the plan is compiled for this workload's own
         parameter values (plan reuse across branches is investigated separately in PERFORMANCE.md).
      2. One warm-up run (compiles the plan and brings the pages into memory: all numbers are warm-cache numbers).
      3. Five measured runs. Logical reads are identical on every run; CPU and elapsed time are reported as medians.
      4. One more run with SET STATISTICS XML ON, saved as <OutputDirectory>\plans\<Phase>\<Workload>.sqlplan.
    Results are written to <OutputDirectory>\results-<Phase>.csv and printed.

.EXAMPLE
    .\database\perf\Measure-Workloads.ps1 -Phase baseline
#>
param(
    [Parameter(Mandatory)] [ValidatePattern('^[a-z0-9-]+$')] [string] $Phase,
    [string] $Server = '.',
    [string] $Database = 'ApexPostDb_Perf',
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\..\docs\performance'),
    [string[]] $Only = @()
)

$ErrorActionPreference = 'Stop'
if ($Database -ne 'ApexPostDb_Perf') { throw 'Measurements run only against ApexPostDb_Perf: the script clears its plan cache.' }

$VarChar48 = { param($name, $value) @{ Name = $name; Type = [Data.SqlDbType]::VarChar; Size = 48; Value = $value } }
$Int = { param($name, $value) @{ Name = $name; Type = [Data.SqlDbType]::Int; Value = $value } }
$Date = { param($name, $value) @{ Name = $name; Type = [Data.SqlDbType]::Date; Value = [datetime]$value } }
$DateTime2 = { param($name, $value) @{ Name = $name; Type = [Data.SqlDbType]::DateTime2; Value = [datetime]$value } }

$shippingVolume = 'SELECT [s].[ZoneCode], [s].[ServiceLevel], COUNT(*) FROM [Shipments] AS [s] WHERE [s].[OriginBranchCode] = @branchCode GROUP BY [s].[ZoneCode], [s].[ServiceLevel]'
$moneyOrders = 'SELECT [i].[Outcome], COUNT(*) FROM [MoneyOrders] AS [m] LEFT JOIN [IdentityVerifications] AS [i] ON [m].[MoneyOrderId] = [i].[MoneyOrderId] WHERE [m].[BranchCode] = @branchCode GROUP BY [i].[Outcome]'
$dashboard = @'
SELECT [b].[Name], [b].[Region], [b].[OpeningHours], [b].[IsOpen], (
    SELECT COUNT(*)
    FROM [Counters] AS [c]
    WHERE [c].[BranchCode] = @branchCode AND [c].[IsOpen] = CAST(1 AS bit)), (
    SELECT COUNT(*)
    FROM [CounterTransactions] AS [c0]
    WHERE [c0].[BranchCode] = @branchCode0 AND [c0].[Status] = 'Paid' AND [c0].[OccurredAtUtc] >= @start AND [c0].[OccurredAtUtc] < @end), (
    SELECT COUNT(*)
    FROM [BranchStock] AS [b0]
    WHERE [b0].[BranchCode] = @branchCode1 AND [b0].[Quantity] <= @lowStockThreshold)
FROM [Branches] AS [b]
WHERE [b].[BranchCode] = @branchCode2
'@
# Non-SARGable experiment: one month of a branch's paid sales, written with YEAR()/MONTH() and as a date range.
$monthNonSargable = 'SELECT COUNT(*) AS PaidSales, SUM([t].[Subtotal] + [t].[Tax]) AS Revenue FROM [CounterTransactions] AS [t] WHERE [t].[BranchCode] = @branchCode AND [t].[Status] = ''Paid'' AND YEAR([t].[OccurredAtUtc]) = @year AND MONTH([t].[OccurredAtUtc]) = @month'
$monthRange = 'SELECT COUNT(*) AS PaidSales, SUM([t].[Subtotal] + [t].[Tax]) AS Revenue FROM [CounterTransactions] AS [t] WHERE [t].[BranchCode] = @branchCode AND [t].[Status] = ''Paid'' AND [t].[OccurredAtUtc] >= @from AND [t].[OccurredAtUtc] < @to'

$workloads = @(
    @{ Name = 'W1-low-stock-BR-0001';        Sql = 'EXEC dbo.usp_LowStockReport @BranchCode = @p0, @Threshold = @p1'; Parameters = @((& $VarChar48 '@p0' 'BR-0001'), (& $Int '@p1' 10)) }
    @{ Name = 'W1-low-stock-BR-0100';        Sql = 'EXEC dbo.usp_LowStockReport @BranchCode = @p0, @Threshold = @p1'; Parameters = @((& $VarChar48 '@p0' 'BR-0100'), (& $Int '@p1' 10)) }
    @{ Name = 'W1-low-stock-all-branches';   Sql = 'EXEC dbo.usp_LowStockReport @BranchCode = NULL, @Threshold = @p1'; Parameters = @((& $Int '@p1' 10)) }
    @{ Name = 'W2-daily-sales-BR-0001';      Sql = 'EXEC dbo.usp_DailyBranchSales @BranchCode = @p0, @Date = @p1'; Parameters = @((& $VarChar48 '@p0' 'BR-0001'), (& $Date '@p1' '2026-10-15')) }
    @{ Name = 'W2-daily-sales-BR-0100';      Sql = 'EXEC dbo.usp_DailyBranchSales @BranchCode = @p0, @Date = @p1'; Parameters = @((& $VarChar48 '@p0' 'BR-0100'), (& $Date '@p1' '2026-10-15')) }
    @{ Name = 'W3-shipping-volume-BR-0001';  Sql = $shippingVolume; Parameters = @((& $VarChar48 '@branchCode' 'BR-0001')) }
    @{ Name = 'W3-shipping-volume-BR-0100';  Sql = $shippingVolume; Parameters = @((& $VarChar48 '@branchCode' 'BR-0100')) }
    @{ Name = 'W4-money-orders-BR-0001';     Sql = $moneyOrders; Parameters = @((& $VarChar48 '@branchCode' 'BR-0001')) }
    @{ Name = 'W4-money-orders-BR-0100';     Sql = $moneyOrders; Parameters = @((& $VarChar48 '@branchCode' 'BR-0100')) }
    @{ Name = 'W5-dashboard-BR-0001';        Sql = $dashboard; Parameters = @((& $VarChar48 '@branchCode' 'BR-0001'), (& $VarChar48 '@branchCode0' 'BR-0001'), (& $DateTime2 '@start' '2026-10-15'), (& $DateTime2 '@end' '2026-10-16'), (& $VarChar48 '@branchCode1' 'BR-0001'), (& $Int '@lowStockThreshold' 10), (& $VarChar48 '@branchCode2' 'BR-0001')) }
    @{ Name = 'W5-dashboard-BR-0100';        Sql = $dashboard; Parameters = @((& $VarChar48 '@branchCode' 'BR-0100'), (& $VarChar48 '@branchCode0' 'BR-0100'), (& $DateTime2 '@start' '2026-10-15'), (& $DateTime2 '@end' '2026-10-16'), (& $VarChar48 '@branchCode1' 'BR-0100'), (& $Int '@lowStockThreshold' 10), (& $VarChar48 '@branchCode2' 'BR-0100')) }
    @{ Name = 'S1-month-YEAR-MONTH-BR-0001'; Sql = $monthNonSargable; Parameters = @((& $VarChar48 '@branchCode' 'BR-0001'), (& $Int '@year' 2026), (& $Int '@month' 9)) }
    @{ Name = 'S1-month-date-range-BR-0001'; Sql = $monthRange; Parameters = @((& $VarChar48 '@branchCode' 'BR-0001'), (& $DateTime2 '@from' '2026-09-01'), (& $DateTime2 '@to' '2026-10-01')) }
)
if ($Only.Count -gt 0) { $workloads = $workloads | Where-Object { $Only -contains $_.Name } }

$planDirectory = Join-Path $OutputDirectory "plans\$Phase"
New-Item -ItemType Directory -Force -Path $planDirectory | Out-Null

$connection = New-Object System.Data.SqlClient.SqlConnection "Server=$Server;Database=$Database;Integrated Security=True;TrustServerCertificate=True;Pooling=False;Application Name=ApexPost.Measure"
$messages = New-Object System.Collections.Generic.List[string]
$connection.add_InfoMessage({ param($sender, $e) foreach ($error in $e.Errors) { $messages.Add($error.Message) } })
$connection.Open()

function Invoke-Batch([string] $sql, [object[]] $parameters, [switch] $CollectXml) {
    $command = $connection.CreateCommand()
    $command.CommandText = $sql
    $command.CommandTimeout = 300
    foreach ($p in $parameters) {
        $parameter = $command.Parameters.Add($p.Name, $p.Type)
        if ($p.Size) { $parameter.Size = $p.Size }
        $parameter.Value = $p.Value
    }
    $plans = @()
    $reader = $command.ExecuteReader()
    do {
        $isPlan = $reader.FieldCount -eq 1 -and $reader.GetName(0) -like '*Showplan*'
        while ($reader.Read()) { if ($isPlan -and $CollectXml) { $plans += $reader.GetString(0) } }
    } while ($reader.NextResult())
    $reader.Close()
    return , $plans
}

function Invoke-Set([string] $sql) { $c = $connection.CreateCommand(); $c.CommandText = $sql; [void]$c.ExecuteNonQuery() }

function Get-PlanShape([string] $planXml) {
    $xml = [xml]$planXml
    $ns = New-Object Xml.XmlNamespaceManager $xml.NameTable
    $ns.AddNamespace('p', 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
    $plan = $xml.SelectSingleNode('//p:QueryPlan', $ns)
    $access = foreach ($op in $xml.SelectNodes('//p:RelOp[p:IndexScan]', $ns)) {
        $object = $op.SelectSingleNode('p:IndexScan/p:Object', $ns)
        $actual = ($op.SelectNodes('p:RunTimeInformation/p:RunTimeCountersPerThread', $ns) | ForEach-Object { [long]$_.ActualRows } | Measure-Object -Sum).Sum
        '{0} {1} (est {2:0.#} / act {3})' -f $op.PhysicalOp, ($object.Index -replace '[\[\]]', ''), [double]$op.EstimateRows, $actual
    }
    $dop = if ($plan.DegreeOfParallelism) { $plan.DegreeOfParallelism } else { '1' }
    return ('DOP {0}; {1}' -f $dop, ($access -join '; '))
}

function Get-Median([double[]] $values) { $sorted = $values | Sort-Object; return $sorted[[int][math]::Floor($sorted.Count / 2)] }

$results = foreach ($workload in $workloads) {
    Invoke-Set 'ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE;'
    [void](Invoke-Batch $workload.Sql $workload.Parameters)   # warm-up and compile

    Invoke-Set 'SET STATISTICS IO ON; SET STATISTICS TIME ON;'
    $cpu = @(); $elapsed = @(); $reads = $null
    for ($run = 1; $run -le 5; $run++) {
        $messages.Clear()
        [void](Invoke-Batch $workload.Sql $workload.Parameters)
        $times = @($messages | Where-Object { $_ -match 'SQL Server Execution Times' })
        $last = $times[-1]
        $cpu += [double]([regex]::Match($last, 'CPU time = (\d+) ms').Groups[1].Value)
        $elapsed += [double]([regex]::Match($last, 'elapsed time = (\d+) ms').Groups[1].Value)
        $reads = $messages | Where-Object { $_ -match "^Table '" } | ForEach-Object {
            $m = [regex]::Match($_, "^Table '([^']+)'\. Scan count (\d+), logical reads (\d+)")
            [pscustomobject]@{ Table = $m.Groups[1].Value; Scans = [int]$m.Groups[2].Value; Reads = [long]$m.Groups[3].Value }
        }
    }
    Invoke-Set 'SET STATISTICS IO OFF; SET STATISTICS TIME OFF;'

    Invoke-Set 'SET STATISTICS XML ON;'
    $plans = Invoke-Batch $workload.Sql $workload.Parameters -CollectXml
    Invoke-Set 'SET STATISTICS XML OFF;'
    $mainPlan = $plans[-1]
    Set-Content -Path (Join-Path $planDirectory "$($workload.Name).sqlplan") -Value $mainPlan -Encoding UTF8

    $byTable = $reads | Where-Object { $_.Reads -gt 0 } | Group-Object Table | ForEach-Object { '{0} {1}' -f $_.Name, ($_.Group | Measure-Object Reads -Sum).Sum }
    [pscustomobject]@{
        Phase          = $Phase
        Workload       = $workload.Name
        LogicalReads   = ($reads | Measure-Object Reads -Sum).Sum
        ReadsByTable   = $byTable -join '; '
        CpuMsMedian    = Get-Median $cpu
        ElapsedMsMedian = Get-Median $elapsed
        CpuMsRuns      = $cpu -join ' '
        PlanShape      = Get-PlanShape $mainPlan
    }
}

$connection.Close()
$csv = Join-Path $OutputDirectory "results-$Phase.csv"
$results | Export-Csv -Path $csv -NoTypeInformation -Encoding UTF8
$results | Format-List
Write-Host "Saved $csv and $(@($workloads).Count) plans in $planDirectory"
