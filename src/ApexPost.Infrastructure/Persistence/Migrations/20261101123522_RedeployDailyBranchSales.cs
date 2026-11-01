using ApexPost.Infrastructure.Persistence.StoredProcedures;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexPost.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Redeploys usp_DailyBranchSales with the day range written as expressions of @Date instead of local variables,
    /// so the optimizer estimates the day's sales from the statistics (PERFORMANCE.md, "Estimated vs actual rows").
    /// Up deploys the body kept below; Down restores the body AddStoredProcedures shipped.
    /// </summary>
    public partial class RedeployDailyBranchSales : Migration
    {
        /// <summary>usp_DailyBranchSales as this migration shipped it.</summary>
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
                -- The bounds are written as expressions of @Date rather than local variables: the optimizer cannot see a local
                -- variable's value, so it guessed thousands of sales for a day that has about a hundred and scanned every line
                -- (PERFORMANCE.md, "Estimated vs actual rows").
                WITH PaidSales AS
                (
                    SELECT t.CounterTransactionId, t.Tax
                    FROM dbo.CounterTransactions AS t
                    WHERE t.BranchCode = @BranchCode
                      AND t.Status = 'Paid'
                      AND t.OccurredAtUtc >= CAST(@Date AS datetime2(3))
                      AND t.OccurredAtUtc < DATEADD(DAY, 1, CAST(@Date AS datetime2(3)))
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

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(StoredProcedureScripts.AsExec(DailyBranchSales));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(StoredProcedureScripts.AsExec(AddStoredProcedures.DailyBranchSales));
        }
    }
}
