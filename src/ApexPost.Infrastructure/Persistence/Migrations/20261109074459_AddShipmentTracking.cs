using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexPost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrackedShipments",
                columns: table => new
                {
                    TrackedShipmentId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TrackingNumber = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RecipientPhone = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    RecipientAddress = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    City = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    WeightKg = table.Column<decimal>(type: "decimal(6,3)", precision: 6, scale: 3, nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false, defaultValue: "system"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedShipments", x => x.TrackedShipmentId);
                    table.UniqueConstraint("AK_TrackedShipments_TrackingNumber", x => x.TrackingNumber);
                    table.CheckConstraint("CK_TrackedShipments_Status", "[Status] IN ('Created', 'PickedUp', 'InTransit', 'OutForDelivery', 'Delivered', 'Failed')");
                    table.CheckConstraint("CK_TrackedShipments_WeightKg", "[WeightKg] >= 0.1 AND [WeightKg] <= 70");
                });

            migrationBuilder.CreateTable(
                name: "TrackingScans",
                columns: table => new
                {
                    TrackingScanId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScanId = table.Column<string>(type: "varchar(42)", unicode: false, maxLength: 42, nullable: false),
                    TrackingNumber = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    ScanType = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CourierId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ScannedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false, defaultValue: "system"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackingScans", x => x.TrackingScanId);
                    table.UniqueConstraint("AK_TrackingScans_ScanId", x => x.ScanId);
                    table.CheckConstraint("CK_TrackingScans_ScanType", "[ScanType] IN ('Pickup', 'InTransit', 'Delivered', 'Failed')");
                    table.ForeignKey(
                        name: "FK_TrackingScans_TrackedShipments_TrackingNumber",
                        column: x => x.TrackingNumber,
                        principalTable: "TrackedShipments",
                        principalColumn: "TrackingNumber",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedShipments_City",
                table: "TrackedShipments",
                column: "City");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedShipments_Status_TrackingNumber",
                table: "TrackedShipments",
                columns: new[] { "Status", "TrackingNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackingScans_TrackingNumber_ScannedAtUtc",
                table: "TrackingScans",
                columns: new[] { "TrackingNumber", "ScannedAtUtc", "ReceivedAtUtc" })
                .Annotation("SqlServer:Include", new[] { "ScanId", "ScanType", "Location", "CourierId", "Notes" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackingScans");

            migrationBuilder.DropTable(
                name: "TrackedShipments");
        }
    }
}
