-- 05  Money orders and identity verifications per branch
--
-- Branches LEFT JOIN MoneyOrders LEFT JOIN IdentityVerifications. Every branch appears, including branches with no
-- money orders. A money order has at most one verification (unique index on IdentityVerifications.MoneyOrderId), so
-- the second LEFT JOIN cannot duplicate money-order rows and SUM(Amount) is not inflated.
--
-- "High value" uses the same rule as the application: Amount > threshold (ApexPost:MoneyOrders, default 1000.00).
-- The period filter is in the ON clause, so branches without orders in the period still appear with zeros.
-- Only masked document references are stored; this report does not read them.
--
-- The second query is the auditor's exception list: HAVING keeps only branches with a failed verification or a
-- high-value order without a successful one.

DECLARE @FromDate date = '2026-09-01';               -- inclusive, by the date the order was recorded
DECLARE @ToDate   date = '2026-10-31';               -- inclusive
DECLARE @HighValueThreshold decimal(18, 2) = 1000.00;

DECLARE @FromUtc datetime2(3) = CAST(@FromDate AS datetime2(3));
DECLARE @ToUtc   datetime2(3) = DATEADD(DAY, 1, CAST(@ToDate AS datetime2(3)));

-- Summary per branch
SELECT
    b.BranchCode,
    b.Name                                                                            AS BranchName,
    COUNT(mo.MoneyOrderId)                                                            AS MoneyOrders,
    CAST(ISNULL(SUM(mo.Amount), 0) AS decimal(18, 2))                                 AS TotalAmount,
    CAST(ISNULL(SUM(mo.Fee), 0) AS decimal(18, 2))                                    AS TotalFees,
    SUM(CASE WHEN mo.Amount > @HighValueThreshold THEN 1 ELSE 0 END)                  AS HighValueOrders,
    COUNT(iv.IdentityVerificationId)                                                  AS Verifications,
    SUM(CASE WHEN iv.Outcome = 'Verified' THEN 1 ELSE 0 END)                          AS Verified,
    SUM(CASE WHEN iv.Outcome = 'Failed' THEN 1 ELSE 0 END)                            AS Failed
FROM dbo.Branches AS b
LEFT JOIN dbo.MoneyOrders AS mo
       ON mo.BranchCode = b.BranchCode
      AND mo.CreatedAtUtc >= @FromUtc
      AND mo.CreatedAtUtc <  @ToUtc
LEFT JOIN dbo.IdentityVerifications AS iv ON iv.MoneyOrderId = mo.MoneyOrderId
GROUP BY b.BranchCode, b.Name
ORDER BY b.BranchCode;

-- Auditor exceptions: branches that turned a customer away or hold a high-value order that is not verified
SELECT
    b.BranchCode,
    SUM(CASE WHEN iv.Outcome = 'Failed' THEN 1 ELSE 0 END)                                         AS FailedVerifications,
    SUM(CASE WHEN mo.Amount > @HighValueThreshold AND ISNULL(iv.Outcome, '') <> 'Verified' THEN 1 ELSE 0 END)
                                                                                                   AS HighValueNotVerified,
    CAST(SUM(CASE WHEN iv.Outcome = 'Failed' THEN mo.Amount ELSE 0 END) AS decimal(18, 2))         AS AmountTurnedAway
FROM dbo.Branches AS b
INNER JOIN dbo.MoneyOrders AS mo ON mo.BranchCode = b.BranchCode
LEFT JOIN dbo.IdentityVerifications AS iv ON iv.MoneyOrderId = mo.MoneyOrderId
WHERE mo.CreatedAtUtc >= @FromUtc
  AND mo.CreatedAtUtc <  @ToUtc
GROUP BY b.BranchCode
HAVING SUM(CASE WHEN iv.Outcome = 'Failed' THEN 1 ELSE 0 END) > 0
    OR SUM(CASE WHEN mo.Amount > @HighValueThreshold AND ISNULL(iv.Outcome, '') <> 'Verified' THEN 1 ELSE 0 END) > 0
ORDER BY b.BranchCode;
