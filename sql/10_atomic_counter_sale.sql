-- 10  Record one counter sale atomically: stock decrement, sale header, lines and payment
--
-- All four writes commit together or not at all:
--   * SET XACT_ABORT ON: any run-time error (constraint violation, conversion error, deadlock victim, THROW) dooms
--     the transaction, so it can never be half committed.
--   * BEGIN TRY ... END TRY / BEGIN CATCH: every failure path ends in the CATCH block.
--   * XACT_STATE() in CATCH: -1 = doomed (only ROLLBACK is allowed), 1 = still committable, 0 = no transaction left.
--     This sale is all-or-nothing, so both -1 and 1 roll back.
--   * THROW (no arguments) re-raises the original error number and message to the caller after the rollback.
--
-- Same rules as the application's checkout:
--   * Prices come from the catalogue (Products), never from the caller. Lines keep the price at the time of sale.
--   * Validation and price lookups run BEFORE BEGIN TRANSACTION, so the transaction holds locks only for its writes.
--   * Stock first, one row at a time in BranchStockId (clustered key) order, then the sale rows. EF Core sends a
--     checkout's commands in this order, so the script and the app lock the same rows in the same order
--     (see "Avoiding deadlocks" in README.md).
--   * The decrement checks stock in the UPDATE itself (WHERE Quantity >= @Quantity). Another session cannot sell
--     the same units between the check and the write. CK_BranchStock_Quantity (Quantity >= 0) is a second guard.
--
-- To see the rollback: ask for more than the stock of the product whose stock row comes second in BranchStockId
-- order. With the demo data that is PRD-010: ('PRD-010', 100000), ('PRD-004', 2). PRD-004 is already decremented
-- when PRD-010 fails; afterwards both rows are unchanged and no sale rows exist.

SET XACT_ABORT ON;
SET NOCOUNT ON;

-- Inputs (fictional demo data)
DECLARE @TransactionCode varchar(48) = CONCAT('TX-SQL-', FORMAT(SYSUTCDATETIME(), 'yyyyMMddHHmmssfff'));
DECLARE @BranchCode      varchar(48) = 'BR-NORTH';
DECLARE @CounterCode     varchar(48) = 'C-01';
DECLARE @ClerkStaffCode  varchar(48) = 'STF-CLERK-01-A';
DECLARE @PaymentMethod   varchar(16) = 'Card';
DECLARE @TaxRate         decimal(9, 4) = 0.0000;   -- ApexPost:Transactions:TaxRate in the demo configuration

DECLARE @Basket TABLE (ProductCode varchar(48) NOT NULL PRIMARY KEY, Quantity int NOT NULL CHECK (Quantity > 0));
INSERT @Basket (ProductCode, Quantity) VALUES ('PRD-010', 1), ('PRD-004', 2);

-- 1. Validate and price the basket (no transaction yet)
IF NOT EXISTS (SELECT 1 FROM dbo.Counters AS c
               INNER JOIN dbo.Branches AS b ON b.BranchCode = c.BranchCode
               WHERE c.BranchCode = @BranchCode AND c.CounterCode = @CounterCode AND c.IsOpen = 1 AND b.IsOpen = 1)
    THROW 50101, 'The branch or counter does not exist or is closed.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.Staff
               WHERE StaffCode = @ClerkStaffCode AND BranchCode = @BranchCode AND IsActive = 1
                 AND RoleId = (SELECT RoleId FROM dbo.Roles WHERE Name = 'CounterClerk'))
    THROW 50102, 'The clerk is not an active counter clerk of this branch.', 1;
IF @PaymentMethod NOT IN ('Cash', 'Card', 'DigitalWallet')
    THROW 50103, 'Unknown payment method.', 1;

DECLARE @Lines TABLE
(
    BranchStockId int           NOT NULL PRIMARY KEY,
    ProductCode   varchar(48)   NOT NULL,
    Description   nvarchar(250) NOT NULL,
    Quantity      int           NOT NULL,
    UnitPrice     decimal(18, 2) NOT NULL
);

INSERT @Lines (BranchStockId, ProductCode, Description, Quantity, UnitPrice)
SELECT s.BranchStockId, p.ProductCode, p.Name, k.Quantity, p.UnitPrice
FROM @Basket AS k
INNER JOIN dbo.Products    AS p ON p.ProductCode = k.ProductCode
INNER JOIN dbo.BranchStock AS s ON s.ProductCode = k.ProductCode AND s.BranchCode = @BranchCode;

IF @@ROWCOUNT <> (SELECT COUNT(*) FROM @Basket)
    THROW 50104, 'A basket line names a product that does not exist or that this branch does not stock.', 1;

DECLARE @Subtotal decimal(18, 2) = (SELECT SUM(UnitPrice * Quantity) FROM @Lines);
DECLARE @Tax      decimal(18, 2) = ROUND(@Subtotal * @TaxRate, 2);
DECLARE @Now      datetime2(3)   = SYSUTCDATETIME();
DECLARE @SaleId   bigint;
DECLARE @StockId  int = 0;
DECLARE @Quantity int;
DECLARE @State    int;

-- 2. Write everything in one short transaction
BEGIN TRY
    BEGIN TRANSACTION;

    -- 2a. Stock rows first, in BranchStockId order
    WHILE 1 = 1
    BEGIN
        SELECT TOP (1) @StockId = BranchStockId, @Quantity = Quantity
        FROM @Lines
        WHERE BranchStockId > @StockId
        ORDER BY BranchStockId;
        IF @@ROWCOUNT = 0 BREAK;

        UPDATE dbo.BranchStock
        SET Quantity = Quantity - @Quantity, ModifiedAtUtc = @Now
        WHERE BranchStockId = @StockId
          AND Quantity >= @Quantity;

        IF @@ROWCOUNT = 0
            THROW 50105, 'Not enough stock for a basket line. Nothing was recorded.', 1;
    END;

    -- 2b. Sale header
    INSERT dbo.CounterTransactions (TransactionCode, BranchCode, CounterCode, ClerkStaffCode, Subtotal, Tax, OccurredAtUtc, Status, CreatedBy)
    VALUES (@TransactionCode, @BranchCode, @CounterCode, @ClerkStaffCode, @Subtotal, @Tax, @Now, 'Paid', @ClerkStaffCode);
    SET @SaleId = SCOPE_IDENTITY();

    -- 2c. Lines
    INSERT dbo.TransactionLines (CounterTransactionId, ProductCode, Description, Quantity, UnitPrice, CreatedBy)
    SELECT @SaleId, ProductCode, Description, Quantity, UnitPrice, @ClerkStaffCode
    FROM @Lines;

    -- 2d. Payment for the full amount
    INSERT dbo.Payments (CounterTransactionId, PaymentMethod, Amount, CreatedBy)
    VALUES (@SaleId, @PaymentMethod, @Subtotal + @Tax, @ClerkStaffCode);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    SET @State = XACT_STATE();
    IF @State <> 0
        ROLLBACK TRANSACTION;

    PRINT CONCAT('Sale ', @TransactionCode, ' rolled back. XACT_STATE() in CATCH was ', @State,
                 CASE @State WHEN -1 THEN ' (doomed).' WHEN 1 THEN ' (committable, rolled back anyway).' ELSE ' (no transaction).' END);
    THROW;
END CATCH;

-- 3. Result
SELECT t.TransactionCode, t.Status, t.Subtotal, t.Tax, pay.PaymentMethod, pay.Amount AS Paid,
       (SELECT COUNT(*) FROM dbo.TransactionLines WHERE CounterTransactionId = t.CounterTransactionId) AS Lines
FROM dbo.CounterTransactions AS t
INNER JOIN dbo.Payments AS pay ON pay.CounterTransactionId = t.CounterTransactionId
WHERE t.CounterTransactionId = @SaleId;

SELECT s.ProductCode, s.Quantity AS StockAfterSale
FROM dbo.BranchStock AS s
INNER JOIN @Lines AS l ON l.BranchStockId = s.BranchStockId
ORDER BY s.ProductCode;
