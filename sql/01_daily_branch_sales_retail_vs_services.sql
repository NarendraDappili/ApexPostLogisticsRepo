-- 01  Daily branch sales, split into retail and services
--
-- One row per branch and UTC day with paid sales in the period:
--   * retail   = lines that reference a catalogue product (TransactionLines.ProductCode IS NOT NULL)
--   * services = shipment and money-order lines (no product)
-- Only sales whose status is still 'Paid' count; voided and refunded sales are excluded, as in usp_DailyBranchSales.
-- Line amounts use the price stored on the line (the price at the time of sale), not today's catalogue price.
--
-- Joins: TransactionLines INNER JOIN the paid sales, then INNER JOIN Branches for the name and region.
-- The lines are summed per sale first, so a sale's Tax is counted once and not once per line.

DECLARE @FromDate date = '2026-10-24';   -- first day, inclusive
DECLARE @ToDate   date = '2026-10-30';   -- last day, inclusive

-- Half-open UTC range: no function on OccurredAtUtc, so the predicate stays SARGable.
DECLARE @FromUtc datetime2(3) = CAST(@FromDate AS datetime2(3));
DECLARE @ToUtc   datetime2(3) = DATEADD(DAY, 1, CAST(@ToDate AS datetime2(3)));

WITH PaidSales AS
(
    SELECT t.CounterTransactionId, t.BranchCode, CAST(t.OccurredAtUtc AS date) AS SalesDate, t.Tax
    FROM dbo.CounterTransactions AS t
    WHERE t.Status = 'Paid'
      AND t.OccurredAtUtc >= @FromUtc
      AND t.OccurredAtUtc <  @ToUtc
),
SaleTotals AS
(
    SELECT
        p.CounterTransactionId,
        p.BranchCode,
        p.SalesDate,
        p.Tax,
        SUM(CASE WHEN l.ProductCode IS NOT NULL THEN l.UnitPrice * l.Quantity ELSE 0 END) AS Retail,
        SUM(CASE WHEN l.ProductCode IS NULL     THEN l.UnitPrice * l.Quantity ELSE 0 END) AS Services
    FROM PaidSales AS p
    INNER JOIN dbo.TransactionLines AS l ON l.CounterTransactionId = p.CounterTransactionId
    GROUP BY p.CounterTransactionId, p.BranchCode, p.SalesDate, p.Tax
)
SELECT
    b.BranchCode,
    b.Name                                 AS BranchName,
    b.Region,
    s.SalesDate,
    CAST(SUM(s.Retail)   AS decimal(18, 2)) AS RetailSales,
    CAST(SUM(s.Services) AS decimal(18, 2)) AS ServiceSales,
    CAST(SUM(s.Tax)      AS decimal(18, 2)) AS Tax,
    COUNT(*)                               AS PaidSales
FROM SaleTotals AS s
INNER JOIN dbo.Branches AS b ON b.BranchCode = s.BranchCode
GROUP BY b.BranchCode, b.Name, b.Region, s.SalesDate
ORDER BY s.SalesDate, b.BranchCode;
