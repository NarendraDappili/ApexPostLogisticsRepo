-- Products at or below @Threshold units. With @BranchCode the report covers one branch; with NULL it covers
-- every branch (head-office view).
CREATE OR ALTER PROCEDURE dbo.usp_LowStockReport
    @BranchCode varchar(48) = NULL,
    @Threshold int
AS
BEGIN
    SET NOCOUNT ON;

    IF @Threshold IS NULL OR @Threshold < 0
        THROW 50011, 'usp_LowStockReport: @Threshold must be zero or more.', 1;
    IF @BranchCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Branches WHERE BranchCode = @BranchCode)
        THROW 50012, 'usp_LowStockReport: the branch does not exist.', 1;

    SELECT s.BranchCode, s.ProductCode, p.Sku, p.Name, s.Quantity
    FROM dbo.BranchStock AS s
    INNER JOIN dbo.Products AS p ON p.ProductCode = s.ProductCode
    WHERE (@BranchCode IS NULL OR s.BranchCode = @BranchCode)
      AND s.Quantity <= @Threshold
    ORDER BY s.BranchCode, s.ProductCode
    -- The optional filter makes one cached plan wrong for half the calls (a seek for one branch, a scan for all).
    -- RECOMPILE builds the plan for the actual values each time; a report runs rarely, so the compile cost is small.
    OPTION (RECOMPILE);
END
