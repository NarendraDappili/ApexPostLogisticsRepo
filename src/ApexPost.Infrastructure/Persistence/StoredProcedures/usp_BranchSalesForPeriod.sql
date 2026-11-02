-- Paid sales of one branch for each UTC day from @FromDate to @ToDate (both included), split into retail (catalogue
-- products) and services (shipments and money orders). Days without a paid sale return no row. Refunded and voided
-- sales are excluded. A single day uses usp_DailyBranchSales; this procedure serves longer periods.
CREATE OR ALTER PROCEDURE dbo.usp_BranchSalesForPeriod
    @BranchCode varchar(48),
    @FromDate date,
    @ToDate date
AS
BEGIN
    SET NOCOUNT ON;

    IF @BranchCode IS NULL OR LTRIM(RTRIM(@BranchCode)) = ''
        THROW 50041, 'usp_BranchSalesForPeriod: @BranchCode is required.', 1;
    IF @FromDate IS NULL OR @ToDate IS NULL
        THROW 50042, 'usp_BranchSalesForPeriod: @FromDate and @ToDate are required.', 1;
    IF @FromDate > @ToDate
        THROW 50044, 'The start date must be on or before the end date.', 1;
    IF DATEDIFF(DAY, @FromDate, @ToDate) >= 366
        THROW 50045, 'The period can be at most 366 days.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Branches WHERE BranchCode = @BranchCode)
        THROW 50043, 'usp_BranchSalesForPeriod: the branch does not exist.', 1;

    -- As in usp_DailyBranchSales, a half-open range keeps the predicate on OccurredAtUtc SARGable, and the bounds are
    -- expressions of the parameters, never local variables. A period can be one week or a whole year, so one cached plan
    -- would suit some calls badly: OPTION (RECOMPILE) compiles each call for its own dates. A report runs rarely, so the
    -- compilation is cheap next to a plan built for the wrong number of rows (PERFORMANCE.md).
    WITH PaidSales AS
    (
        SELECT t.CounterTransactionId, t.Tax, CAST(t.OccurredAtUtc AS date) AS SalesDate
        FROM dbo.CounterTransactions AS t
        WHERE t.BranchCode = @BranchCode
          AND t.Status = 'Paid'
          AND t.OccurredAtUtc >= CAST(@FromDate AS datetime2(3))
          AND t.OccurredAtUtc < DATEADD(DAY, 1, CAST(@ToDate AS datetime2(3)))
    ),
    DayTotals AS
    (
        SELECT p.SalesDate, SUM(p.Tax) AS Tax, COUNT(*) AS PaidTransactionCount
        FROM PaidSales AS p
        GROUP BY p.SalesDate
    ),
    LineTotals AS
    (
        SELECT
            p.SalesDate,
            SUM(CASE WHEN l.ProductCode IS NOT NULL THEN l.UnitPrice * l.Quantity ELSE 0 END) AS RetailSales,
            SUM(CASE WHEN l.ProductCode IS NULL THEN l.UnitPrice * l.Quantity ELSE 0 END) AS ServiceSales
        FROM dbo.TransactionLines AS l
        INNER JOIN PaidSales AS p ON p.CounterTransactionId = l.CounterTransactionId
        GROUP BY p.SalesDate
    )
    SELECT
        @BranchCode AS BranchCode,
        d.SalesDate,
        CAST(ISNULL(lt.RetailSales, 0) AS decimal(18, 2)) AS RetailSales,
        CAST(ISNULL(lt.ServiceSales, 0) AS decimal(18, 2)) AS ServiceSales,
        CAST(d.Tax AS decimal(18, 2)) AS Tax,
        d.PaidTransactionCount
    FROM DayTotals AS d
    LEFT JOIN LineTotals AS lt ON lt.SalesDate = d.SalesDate
    ORDER BY d.SalesDate
    OPTION (RECOMPILE);
END
