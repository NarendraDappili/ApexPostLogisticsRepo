-- Query Store evidence for the two estimated-vs-actual investigations (PERFORMANCE.md, "Query Store").
--
-- SET STATISTICS shows one execution in one session. Query Store records every execution of every plan in the
-- database, with the plan itself, so it shows the same problems the way a DBA would find them in production:
--   A. usp_DailyBranchSales: the shipped body against a probe copy of the body it replaced (local variables).
--   B. The shipping report: one cached plan, compiled for BR-0100, reused by the flagship BR-0001.
--
-- Run once against the performance database; it changes nothing but Query Store and one probe procedure, which it
-- drops again:
--   sqlcmd -S . -E -C -d ApexPostDb_Perf -I -i database\perf\06-query-store.sql

SET NOCOUNT ON;
IF DB_NAME() <> N'ApexPostDb_Perf'
    THROW 50911, 'This script clears Query Store and the plan cache, so it runs only in ApexPostDb_Perf.', 1;

-- Full-scan statistics, as after the load. Queries run since then may have auto-created sampled column statistics,
-- and the local-variable guess in workload A depends on which statistics exist, so refresh them for a stable result.
UPDATE STATISTICS dbo.CounterTransactions WITH FULLSCAN;
UPDATE STATISTICS dbo.TransactionLines WITH FULLSCAN;
UPDATE STATISTICS dbo.Shipments WITH FULLSCAN;

-- Capture every query (AUTO would skip the cheap ones) and start from an empty store and plan cache.
ALTER DATABASE CURRENT SET QUERY_STORE = ON (OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = ALL);
ALTER DATABASE CURRENT SET QUERY_STORE CLEAR;
ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE;
GO

-- A probe copy of usp_DailyBranchSales as AddStoredProcedures first shipped it: the day range in local variables.
-- Measurement only; the real procedure is never changed.
CREATE OR ALTER PROCEDURE dbo.usp_DailyBranchSales_LocalVariablesProbe
    @BranchCode varchar(48),
    @Date date
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @From datetime2(3) = CAST(@Date AS datetime2(3));
    DECLARE @To datetime2(3) = DATEADD(DAY, 1, @From);

    WITH PaidSales AS
    (
        SELECT t.CounterTransactionId, t.Tax
        FROM dbo.CounterTransactions AS t
        WHERE t.BranchCode = @BranchCode
          AND t.Status = 'Paid'
          AND t.OccurredAtUtc >= @From
          AND t.OccurredAtUtc < @To
    ),
    LineTotals AS
    (
        SELECT
            SUM(CASE WHEN l.ProductCode IS NOT NULL THEN l.UnitPrice * l.Quantity ELSE 0 END) AS RetailSales,
            SUM(CASE WHEN l.ProductCode IS NULL THEN l.UnitPrice * l.Quantity ELSE 0 END) AS ServiceSales
        FROM dbo.TransactionLines AS l
        INNER JOIN PaidSales AS p ON p.CounterTransactionId = l.CounterTransactionId
    )
    SELECT
        @BranchCode AS BranchCode,
        @Date AS SalesDate,
        CAST(ISNULL(lt.RetailSales, 0) AS decimal(18, 2)) AS RetailSales,
        CAST(ISNULL(lt.ServiceSales, 0) AS decimal(18, 2)) AS ServiceSales,
        CAST(ISNULL((SELECT SUM(p.Tax) FROM PaidSales AS p), 0) AS decimal(18, 2)) AS Tax,
        (SELECT COUNT(*) FROM PaidSales) AS PaidTransactionCount
    FROM LineTotals AS lt;
END
GO

-- A. Five calls of each body for the flagship's 15 October (110 paid sales), as in Investigation 1.
DECLARE @Results table (BranchCode varchar(48), SalesDate date, RetailSales decimal(18, 2), ServiceSales decimal(18, 2), Tax decimal(18, 2), PaidTransactionCount int);
DECLARE @i int = 0;
WHILE @i < 5
BEGIN
    INSERT @Results EXEC dbo.usp_DailyBranchSales_LocalVariablesProbe @BranchCode = 'BR-0001', @Date = '2026-10-15';
    INSERT @Results EXEC dbo.usp_DailyBranchSales @BranchCode = 'BR-0001', @Date = '2026-10-15';
    SET @i += 1;
END;

-- B. The shipping report exactly as EF Core sends it: compiled for BR-0100 (403 shipments), then run five times for
-- BR-0001 (20,000 shipments) on the same cached plan, as in Investigation 2.
DECLARE @sql nvarchar(max) = N'SELECT [s].[ZoneCode], [s].[ServiceLevel], COUNT(*)
FROM [Shipments] AS [s]
WHERE [s].[OriginBranchCode] = @branchCode
GROUP BY [s].[ZoneCode], [s].[ServiceLevel]';
DECLARE @Volume table (ZoneCode varchar(48), ServiceLevel varchar(16), Shipments int);
INSERT @Volume EXEC sys.sp_executesql @sql, N'@branchCode varchar(48)', @branchCode = 'BR-0100';
SET @i = 0;
WHILE @i < 5
BEGIN
    INSERT @Volume EXEC sys.sp_executesql @sql, N'@branchCode varchar(48)', @branchCode = 'BR-0001';
    SET @i += 1;
END;
GO

-- What Query Store recorded: one row per query and plan. Logical reads are per execution; CPU is in microseconds.
-- The estimate and the compiled parameter value are read from the plan Query Store stored.
WITH XMLNAMESPACES (DEFAULT 'http://schemas.microsoft.com/sqlserver/2004/07/showplan'),
Recorded AS
(
    SELECT
        CASE
            WHEN q.object_id = OBJECT_ID(N'dbo.usp_DailyBranchSales_LocalVariablesProbe') THEN 'A. local variables (replaced body)'
            WHEN q.object_id = OBJECT_ID(N'dbo.usp_DailyBranchSales') THEN 'A. expressions of @Date (shipped body)'
            ELSE 'B. shipping report (EF Core)'
        END AS workload,
        q.query_id,
        p.plan_id,
        TRY_CAST(p.query_plan AS xml) AS plan_xml,
        SUM(rs.count_executions) AS executions,
        CAST(SUM(rs.avg_logical_io_reads * rs.count_executions) / SUM(rs.count_executions) AS decimal(12, 1)) AS avg_reads,
        MIN(rs.min_logical_io_reads) AS min_reads,
        MAX(rs.max_logical_io_reads) AS max_reads,
        CAST(SUM(rs.avg_cpu_time * rs.count_executions) / SUM(rs.count_executions) AS decimal(12, 0)) AS avg_cpu_us,
        MAX(rs.max_query_max_used_memory) * 8 AS max_memory_kb
    FROM sys.query_store_query AS q
    INNER JOIN sys.query_store_query_text AS qt ON qt.query_text_id = q.query_text_id
    INNER JOIN sys.query_store_plan AS p ON p.query_id = q.query_id
    INNER JOIN sys.query_store_runtime_stats AS rs ON rs.plan_id = p.plan_id
    WHERE (q.object_id IN (OBJECT_ID(N'dbo.usp_DailyBranchSales'), OBJECT_ID(N'dbo.usp_DailyBranchSales_LocalVariablesProbe'))
           AND qt.query_sql_text LIKE N'%WITH PaidSales%')
       OR qt.query_sql_text LIKE N'%FROM \[Shipments\] AS \[s\]%' ESCAPE N'\'
    GROUP BY q.query_id, q.object_id, p.plan_id, p.query_plan
)
SELECT
    workload,
    query_id,
    plan_id,
    executions,
    avg_reads,
    min_reads,
    max_reads,
    avg_cpu_us,
    max_memory_kb,
    -- The estimate on the access to the filtered table: sales for A, shipments for B.
    plan_xml.value('(//RelOp[IndexScan/Object[@Table="[CounterTransactions]" or @Table="[Shipments]"]]/@EstimateRows)[1]', 'float') AS estimated_rows,
    plan_xml.value('(//ParameterList/ColumnReference[@Column="@branchCode"]/@ParameterCompiledValue)[1]', 'varchar(48)') AS compiled_for
FROM Recorded
ORDER BY workload, query_id, plan_id;
GO

DROP PROCEDURE dbo.usp_DailyBranchSales_LocalVariablesProbe;
