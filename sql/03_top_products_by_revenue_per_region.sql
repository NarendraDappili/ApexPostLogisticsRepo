-- 03  Top 10 catalogue products by revenue per region
--
-- Revenue = SUM(line UnitPrice * Quantity) on paid sales in the period, using the price at the time of sale.
-- INNER JOIN to Products keeps only catalogue lines; service lines (shipments, money orders) have no ProductCode.
-- HAVING drops products whose period revenue is zero (for example, items given away at a price of 0).
--
-- Ranking: ROW_NUMBER with a deterministic tie-break (units sold, then product code), so each region returns at
-- most exactly 10 rows. RANK() would keep every product tied on 10th place and could return more than 10 rows.

DECLARE @FromDate date = '2026-09-01';   -- inclusive
DECLARE @ToDate   date = '2026-10-31';   -- inclusive

DECLARE @FromUtc datetime2(3) = CAST(@FromDate AS datetime2(3));
DECLARE @ToUtc   datetime2(3) = DATEADD(DAY, 1, CAST(@ToDate AS datetime2(3)));

WITH ProductRevenue AS
(
    SELECT
        b.Region,
        p.ProductCode,
        p.Sku,
        p.Name,
        SUM(l.Quantity)               AS UnitsSold,
        SUM(l.UnitPrice * l.Quantity) AS Revenue
    FROM dbo.TransactionLines AS l
    INNER JOIN dbo.CounterTransactions AS t ON t.CounterTransactionId = l.CounterTransactionId
    INNER JOIN dbo.Branches            AS b ON b.BranchCode = t.BranchCode
    INNER JOIN dbo.Products            AS p ON p.ProductCode = l.ProductCode
    WHERE t.Status = 'Paid'
      AND t.OccurredAtUtc >= @FromUtc
      AND t.OccurredAtUtc <  @ToUtc
    GROUP BY b.Region, p.ProductCode, p.Sku, p.Name
    HAVING SUM(l.UnitPrice * l.Quantity) > 0
),
Ranked AS
(
    SELECT
        r.*,
        ROW_NUMBER() OVER (PARTITION BY r.Region ORDER BY r.Revenue DESC, r.UnitsSold DESC, r.ProductCode) AS RevenueRank
    FROM ProductRevenue AS r
)
SELECT
    Region,
    RevenueRank,
    ProductCode,
    Sku,
    Name,
    UnitsSold,
    CAST(Revenue AS decimal(18, 2)) AS Revenue
FROM Ranked
WHERE RevenueRank <= 10
ORDER BY Region, RevenueRank;
