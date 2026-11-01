-- Manual stock correction: changes one branch stock row by @Delta and records who did it and why in
-- StockAdjustments, both or neither. Returns the new quantity. Stock can never go below zero.
-- Only an active Branch Manager of the branch may adjust its stock.
-- Works on its own or inside a caller's transaction: it only starts and commits a transaction when none is open.
CREATE OR ALTER PROCEDURE dbo.usp_AdjustStock
    @BranchCode varchar(48),
    @Sku varchar(32),
    @Delta int,
    @Reason nvarchar(200),
    @ChangedBy varchar(48)
AS
BEGIN
    SET NOCOUNT ON;
    -- Any run-time error dooms the transaction, so no partial adjustment can be committed.
    SET XACT_ABORT ON;

    IF @BranchCode IS NULL OR LTRIM(RTRIM(@BranchCode)) = ''
        THROW 50021, 'usp_AdjustStock: @BranchCode is required.', 1;
    IF @Sku IS NULL OR LTRIM(RTRIM(@Sku)) = ''
        THROW 50022, 'usp_AdjustStock: @Sku is required.', 1;
    IF @Delta IS NULL OR @Delta = 0
        THROW 50023, 'A stock adjustment cannot be zero.', 1;
    IF @Reason IS NULL OR LTRIM(RTRIM(@Reason)) = ''
        THROW 50024, 'An adjustment reason is required.', 1;
    -- The same rule as StockAdjustmentService, so a correction from SSMS or a script is held to it too.
    IF NOT EXISTS (SELECT 1
                   FROM dbo.Staff AS s
                   INNER JOIN dbo.Roles AS r ON r.RoleId = s.RoleId
                   WHERE s.StaffCode = @ChangedBy
                     AND s.IsActive = 1
                     AND r.Name = 'BranchManager'
                     AND s.BranchCode = @BranchCode)
        THROW 50025, 'A Branch Manager assigned to the branch must make stock adjustments.', 1;

    DECLARE @StartedTransaction bit = 0;
    BEGIN TRY
        IF @@TRANCOUNT = 0
        BEGIN
            BEGIN TRANSACTION;
            SET @StartedTransaction = 1;
        END;

        DECLARE @ProductCode varchar(48) = (SELECT ProductCode FROM dbo.Products WHERE Sku = @Sku);
        IF @ProductCode IS NULL
            THROW 50026, 'No product has this SKU.', 1;

        -- The quantity check is part of the UPDATE, so a concurrent change cannot slip between check and write.
        UPDATE dbo.BranchStock
        SET Quantity = Quantity + @Delta,
            ModifiedAtUtc = SYSUTCDATETIME()
        WHERE BranchCode = @BranchCode
          AND ProductCode = @ProductCode
          AND Quantity + @Delta >= 0;

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM dbo.BranchStock WHERE BranchCode = @BranchCode AND ProductCode = @ProductCode)
                THROW 50027, 'The branch does not stock this product.', 1;
            THROW 50028, 'Stock cannot be reduced below zero.', 1;
        END;

        INSERT INTO dbo.StockAdjustments (BranchCode, ProductCode, Delta, Reason, ChangedByStaffCode, OccurredAtUtc, CreatedBy)
        VALUES (@BranchCode, @ProductCode, @Delta, LTRIM(RTRIM(@Reason)), @ChangedBy, SYSUTCDATETIME(), @ChangedBy);

        SELECT Quantity AS NewQuantity
        FROM dbo.BranchStock
        WHERE BranchCode = @BranchCode AND ProductCode = @ProductCode;

        IF @StartedTransaction = 1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @StartedTransaction = 1 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END
