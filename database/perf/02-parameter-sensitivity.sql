-- Estimated vs actual rows: parameter sensitivity of the shipping-volume report (PERFORMANCE.md).
--
-- The application sends the report as a parameterised sp_executesql batch (the SQL below is the exact text EF Core
-- generates; see GeneratedSqlTests). SQL Server compiles the plan for the FIRST parameter value it sees and reuses it.
-- BR-0100 has 403 shipments, the flagship BR-0001 has 20,000.
--
-- Run in SSMS against ApexPostDb_Perf with "Include Actual Execution Plan" (Ctrl+M), or let
-- Measure-Workloads.ps1 style tooling capture SET STATISTICS XML. Compare EstimateRows with ActualRows on the
-- index operators of each step.

SET NOCOUNT ON;

DECLARE @sql nvarchar(max) = N'SELECT [s].[ZoneCode], [s].[ServiceLevel], COUNT(*)
FROM [Shipments] AS [s]
WHERE [s].[OriginBranchCode] = @branchCode
GROUP BY [s].[ZoneCode], [s].[ServiceLevel]';

-- STEP 1: start clean, then compile the plan for the small branch.
ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE;
EXEC sys.sp_executesql @sql, N'@branchCode varchar(48)', @branchCode = 'BR-0100';

-- STEP 2: the flagship branch reuses that plan.
SET STATISTICS IO, TIME ON;
EXEC sys.sp_executesql @sql, N'@branchCode varchar(48)', @branchCode = 'BR-0001';
SET STATISTICS IO, TIME OFF;

-- STEP 3: which plans are cached for the statement, and were they reused?
-- With the Parameter Sensitive Plan optimization (compatibility level 160+) one statement can have several
-- query variants, each with its own plan; the variant's text ends with an OPTION (PLAN PER VALUE ...) hint.
SELECT
    qs.execution_count,
    qs.plan_generation_num,
    qs.last_logical_reads,
    qs.last_rows,
    CASE WHEN st.text LIKE N'%PLAN PER VALUE%' THEN N'PSP query variant' ELSE N'single plan' END AS plan_kind,
    LEFT(st.text, 200) AS statement_text
FROM sys.dm_exec_query_stats AS qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS st
WHERE st.text LIKE N'%FROM \[Shipments\] AS \[s\]%' ESCAPE N'\'
  AND st.text NOT LIKE N'%dm_exec_query_stats%';
