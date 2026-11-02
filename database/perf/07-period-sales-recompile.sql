-- Why usp_BranchSalesForPeriod carries OPTION (RECOMPILE) (PERFORMANCE.md, "Investigation 4").
--
-- A period can be one day or a whole year, and the branch can have 13 sales a week or 40,000 a year. Without the hint,
-- the first call's plan is cached and reused for every later period. The script runs:
--   A. the procedure as shipped, for a short and a long period;
--   B. a probe copy without the hint: a small period compiles the plan, then a year at the flagship branch reuses it.
-- The probe is built from the deployed body (only the hint is removed), so it cannot drift from the real procedure,
-- and it is dropped at the end. Run against the performance database:
--   sqlcmd -S . -E -C -d ApexPostDb_Perf -I -i database\perf\07-period-sales-recompile.sql

SET NOCOUNT ON;
IF DB_NAME() <> N'ApexPostDb_Perf'
    THROW 50912, 'This script clears the plan cache, so it runs only in ApexPostDb_Perf.', 1;

DECLARE @probe nvarchar(max) = REPLACE(REPLACE(
    OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_BranchSalesForPeriod')),
    N'dbo.usp_BranchSalesForPeriod', N'dbo.probe_BranchSalesForPeriod_NoRecompile'),
    N'OPTION (RECOMPILE);', N';');
IF @probe IS NULL OR @probe NOT LIKE N'%probe_BranchSalesForPeriod_NoRecompile%' OR @probe LIKE N'%OPTION (RECOMPILE);%'
    THROW 50913, 'usp_BranchSalesForPeriod is missing or its body changed; update this script.', 1;
EXEC (@probe);

DBCC FREEPROCCACHE WITH NO_INFOMSGS;
UPDATE STATISTICS dbo.CounterTransactions WITH FULLSCAN;
UPDATE STATISTICS dbo.TransactionLines WITH FULLSCAN;
SET STATISTICS IO ON;

PRINT N'=== A. As shipped (RECOMPILE): BR-0001, 7 days';
EXEC dbo.usp_BranchSalesForPeriod @BranchCode = 'BR-0001', @FromDate = '2026-10-24', @ToDate = '2026-10-30';
PRINT N'=== A. As shipped (RECOMPILE): BR-0001, 365 days';
EXEC dbo.usp_BranchSalesForPeriod @BranchCode = 'BR-0001', @FromDate = '2025-10-31', @ToDate = '2026-10-30';
PRINT N'=== A. As shipped (RECOMPILE): BR-0100, 7 days';
EXEC dbo.usp_BranchSalesForPeriod @BranchCode = 'BR-0100', @FromDate = '2026-10-24', @ToDate = '2026-10-30';
PRINT N'=== B. Without the hint: BR-0100, 7 days (compiles the cached plan)';
EXEC dbo.probe_BranchSalesForPeriod_NoRecompile @BranchCode = 'BR-0100', @FromDate = '2026-10-24', @ToDate = '2026-10-30';
PRINT N'=== B. Without the hint: BR-0001, 365 days (reuses that plan)';
EXEC dbo.probe_BranchSalesForPeriod_NoRecompile @BranchCode = 'BR-0001', @FromDate = '2025-10-31', @ToDate = '2026-10-30';

SET STATISTICS IO OFF;
DROP PROCEDURE dbo.probe_BranchSalesForPeriod_NoRecompile;
