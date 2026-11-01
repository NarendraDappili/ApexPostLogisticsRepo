using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexPost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkloadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TransactionLines_CounterTransactionId",
                table: "TransactionLines");

            migrationBuilder.DropIndex(
                name: "IX_Shipments_OriginBranchCode",
                table: "Shipments");

            migrationBuilder.DropIndex(
                name: "IX_IdentityVerifications_MoneyOrderId",
                table: "IdentityVerifications");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLines_CounterTransactionId",
                table: "TransactionLines",
                column: "CounterTransactionId")
                .Annotation("SqlServer:Include", new[] { "ProductCode", "Quantity", "UnitPrice" });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_OriginBranchCode_ZoneCode_ServiceLevel",
                table: "Shipments",
                columns: new[] { "OriginBranchCode", "ZoneCode", "ServiceLevel" });

            migrationBuilder.CreateIndex(
                name: "IX_IdentityVerifications_MoneyOrderId",
                table: "IdentityVerifications",
                column: "MoneyOrderId",
                unique: true)
                .Annotation("SqlServer:Include", new[] { "Outcome" });

            migrationBuilder.CreateIndex(
                name: "IX_CounterTransactions_BranchCode_OccurredAtUtc",
                table: "CounterTransactions",
                columns: new[] { "BranchCode", "OccurredAtUtc" })
                .Annotation("SqlServer:Include", new[] { "Status", "Subtotal", "Tax" });

            migrationBuilder.CreateIndex(
                name: "IX_BranchStock_BranchCode_Quantity",
                table: "BranchStock",
                columns: new[] { "BranchCode", "Quantity" })
                .Annotation("SqlServer:Include", new[] { "ProductCode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TransactionLines_CounterTransactionId",
                table: "TransactionLines");

            migrationBuilder.DropIndex(
                name: "IX_Shipments_OriginBranchCode_ZoneCode_ServiceLevel",
                table: "Shipments");

            migrationBuilder.DropIndex(
                name: "IX_IdentityVerifications_MoneyOrderId",
                table: "IdentityVerifications");

            migrationBuilder.DropIndex(
                name: "IX_CounterTransactions_BranchCode_OccurredAtUtc",
                table: "CounterTransactions");

            migrationBuilder.DropIndex(
                name: "IX_BranchStock_BranchCode_Quantity",
                table: "BranchStock");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLines_CounterTransactionId",
                table: "TransactionLines",
                column: "CounterTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_OriginBranchCode",
                table: "Shipments",
                column: "OriginBranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_IdentityVerifications_MoneyOrderId",
                table: "IdentityVerifications",
                column: "MoneyOrderId",
                unique: true);
        }
    }
}
