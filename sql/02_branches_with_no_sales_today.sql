-- 02  Open branches with no paid sales today (LEFT JOIN)
--
-- Every open branch is kept by the LEFT JOIN. Branches that have no matching sale get NULLs on the sales side,
-- so COUNT(t.CounterTransactionId) is 0 for them and HAVING keeps only those.
--
-- The sales conditions (status and day) belong in the ON clause. In the WHERE clause they would discard the
-- NULL rows and turn the LEFT JOIN into an INNER JOIN, so the query would never return a branch.
-- (NOT EXISTS is an equivalent alternative. The LEFT JOIN form is used here because it can also report counts.)
--
-- "Today" is the UTC day, the same day boundary the application and usp_DailyBranchSales use.

DECLARE @Day date = CAST(SYSUTCDATETIME() AS date);
-- The demo history ends on the day it was seeded. To see the rotation in the demo data, use that day instead:
-- SET @Day = '2026-10-29';

DECLARE @FromUtc datetime2(3) = CAST(@Day AS datetime2(3));
DECLARE @ToUtc   datetime2(3) = DATEADD(DAY, 1, @FromUtc);

SELECT
    b.BranchCode,
    b.Name                         AS BranchName,
    b.Region,
    @Day                           AS SalesDate,
    COUNT(t.CounterTransactionId)  AS PaidSales
FROM dbo.Branches AS b
LEFT JOIN dbo.CounterTransactions AS t
       ON t.BranchCode = b.BranchCode
      AND t.Status = 'Paid'
      AND t.OccurredAtUtc >= @FromUtc
      AND t.OccurredAtUtc <  @ToUtc
WHERE b.IsOpen = 1
GROUP BY b.BranchCode, b.Name, b.Region
HAVING COUNT(t.CounterTransactionId) = 0
ORDER BY b.BranchCode;
