using ApexPost.Infrastructure.Persistence.StoredProcedures;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexPost.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Deploys the reporting and stock-adjustment stored procedures. Each body is kept here exactly as this migration
    /// shipped it, so replaying the migration on a new database always deploys the same text. Later changes come in
    /// their own migrations. Each runs through EXEC because CREATE PROCEDURE must start a batch and idempotent scripts
    /// wrap migrations in IF blocks.
    /// </summary>
    public partial class AddStoredProcedures : Migration
    {
        /// <summary>usp_DailyBranchSales as first shipped; replaced by RedeployDailyBranchSales.</summary>
        internal const string DailyBranchSales = """
            -- Paid sales of one branch on one UTC day, split into retail (catalogue products) and services
            -- (shipments and money orders). Refunded and voided sales are excluded.
            CREATE OR ALTER PROCEDURE dbo.usp_DailyBranchSales
                @BranchCode varchar(48),
                @Date date
            AS
            BEGIN
                SET NOCOUNT ON;

                IF @BranchCode IS NULL OR LTRIM(RTRIM(@BranchCode)) = ''
                    THROW 50001, 'usp_DailyBranchSales: @BranchCode is required.', 1;
                IF @Date IS NULL
                    THROW 50002, 'usp_DailyBranchSales: @Date is required.', 1;
                IF NOT EXISTS (SELECT 1 FROM dbo.Branches WHERE BranchCode = @BranchCode)
                    THROW 50003, 'usp_DailyBranchSales: the branch does not exist.', 1;

                -- A half-open range keeps the predicate on OccurredAtUtc SARGable (no function applied to the column).
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
            """;

        /// <summary>usp_LowStockReport as first shipped.</summary>
        internal const string LowStockReport = """
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
            """;

        /// <summary>usp_AdjustStock as first shipped; replaced by AdjustStockRequiresBranchManager.</summary>
        internal const string AdjustStock = """
            -- Manual stock correction: changes one branch stock row by @Delta and records who did it and why in
            -- StockAdjustments, both or neither. Returns the new quantity. Stock can never go below zero.
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
                IF NOT EXISTS (SELECT 1 FROM dbo.Staff WHERE StaffCode = @ChangedBy AND IsActive = 1)
                    THROW 50025, 'The adjustment must be made by an active staff member.', 1;

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
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(StoredProcedureScripts.AsExec(DailyBranchSales));
            migrationBuilder.Sql(StoredProcedureScripts.AsExec(LowStockReport));
            migrationBuilder.Sql(StoredProcedureScripts.AsExec(AdjustStock));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_AdjustStock;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_LowStockReport;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_DailyBranchSales;");
        }
    }
}
