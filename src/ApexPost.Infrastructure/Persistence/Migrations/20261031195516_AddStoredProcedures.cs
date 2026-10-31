using ApexPost.Infrastructure.Persistence.StoredProcedures;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexPost.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Deploys the reporting and stock-adjustment stored procedures from the embedded CREATE OR ALTER scripts.
    /// Each runs through EXEC because CREATE PROCEDURE must start a batch and idempotent scripts wrap migrations in IF blocks.
    /// </summary>
    public partial class AddStoredProcedures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(StoredProcedureScripts.ReadAsExec(StoredProcedureScripts.DailyBranchSales));
            migrationBuilder.Sql(StoredProcedureScripts.ReadAsExec(StoredProcedureScripts.LowStockReport));
            migrationBuilder.Sql(StoredProcedureScripts.ReadAsExec(StoredProcedureScripts.AdjustStock));
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
