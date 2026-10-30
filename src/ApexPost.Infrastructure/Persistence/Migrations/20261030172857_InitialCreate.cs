using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ApexPost.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    AuditEntryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    StaffCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: true),
                    Role = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    BranchCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: true),
                    Controller = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    RouteValuesJson = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    DurationMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    CorrelationId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false, defaultValue: "system"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.AuditEntryId);
                    table.CheckConstraint("CK_AuditEntries_DurationMilliseconds", "[DurationMilliseconds] >= 0");
                    table.CheckConstraint("CK_AuditEntries_Outcome", "[Outcome] IN ('Succeeded', 'Failed')");
                    table.CheckConstraint("CK_AuditEntries_RouteValuesJson", "ISJSON([RouteValuesJson]) = 1");
                    table.CheckConstraint("CK_AuditEntries_StatusCode", "[StatusCode] BETWEEN 100 AND 599");
                });

            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    BranchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    OpeningHours = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    IsOpen = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false, defaultValue: "system"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.BranchId);
                    table.UniqueConstraint("AK_Branches_BranchCode", x => x.BranchCode);
                });

            migrationBuilder.CreateTable(
                name: "ProductCategories",
                columns: table => new
                {
                    ProductCategoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CategoryCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false, defaultValue: "system"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCategories", x => x.ProductCategoryId);
                    table.UniqueConstraint("AK_ProductCategories_CategoryCode", x => x.CategoryCode);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    RoleId = table.Column<byte>(type: "tinyint", nullable: false),
                    Name = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false, defaultValue: "system"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.RoleId);
                });

            migrationBuilder.CreateTable(
                name: "ShippingZones",
                columns: table => new
                {
                    ShippingZoneId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ZoneCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false, defaultValue: "system"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingZones", x => x.ShippingZoneId);
                    table.UniqueConstraint("AK_ShippingZones_ZoneCode", x => x.ZoneCode);
                });

            migrationBuilder.CreateTable(
                name: "Counters",
                columns: table => new
                {
                    CounterId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CounterCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    BranchCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    IsOpen = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false, defaultValue: "system"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Counters", x => x.CounterId);
                    table.UniqueConstraint("AK_Counters_BranchCode_CounterCode", x => new { x.BranchCode, x.CounterCode });
                    table.ForeignKey(
                        name: "FK_Counters_Branches_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "Branches",
                        principalColumn: "BranchCode",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MoneyOrders",
                columns: table => new
                {
                    MoneyOrderId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MoneyOrderCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    BranchCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Sender = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    TransactionContextCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false, defaultValue: "system"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoneyOrders", x => x.MoneyOrderId);
                    table.UniqueConstraint("AK_MoneyOrders_MoneyOrderCode", x => x.MoneyOrderCode);
                    table.CheckConstraint("CK_MoneyOrders_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_MoneyOrders_Fee", "[Fee] >= 0");
                    table.ForeignKey(
                        name: "FK_MoneyOrders_Branches_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "Branches",
                        principalColumn: "BranchCode",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    ProductId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    Sku = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    CategoryCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false, defaultValue: "system"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.ProductId);
                    table.UniqueConstraint("AK_Products_ProductCode", x => x.ProductCode);
                    table.CheckConstraint("CK_Products_UnitPrice", "[UnitPrice] >= 0");
                    table.ForeignKey(
                        name: "FK_Products_ProductCategories_CategoryCode",
                        column: x => x.CategoryCode,
                        principalTable: "ProductCategories",
                        principalColumn: "CategoryCode",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Staff",
                columns: table => new
                {
                    StaffMemberId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StaffCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Email = table.Column<string>(type: "varchar(254)", unicode: false, maxLength: 254, nullable: true),
                    RoleId = table.Column<byte>(type: "tinyint", nullable: false),
                    BranchCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    PasswordHash = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false, defaultValue: "system"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Staff", x => x.StaffMemberId);
                    table.UniqueConstraint("AK_Staff_StaffCode", x => x.StaffCode);
                    table.ForeignKey(
                        name: "FK_Staff_Branches_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "Branches",
                        principalColumn: "BranchCode",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Staff_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Shipments",
                columns: table => new
                {
                    ShipmentId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShipmentCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    OriginBranchCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    ZoneCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    WeightKg = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    LengthCm = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    WidthCm = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    HeightCm = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    ShipmentType = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    ServiceLevel = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    TransactionCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false, defaultValue: "system"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipments", x => x.ShipmentId);
                    table.UniqueConstraint("AK_Shipments_ShipmentCode", x => x.ShipmentCode);
                    table.CheckConstraint("CK_Shipments_Dimensions", "[LengthCm] > 0 AND [WidthCm] > 0 AND [HeightCm] > 0");
                    table.CheckConstraint("CK_Shipments_ServiceLevel", "[ServiceLevel] IN ('Standard', 'Express', 'Registered')");
                    table.CheckConstraint("CK_Shipments_ShipmentType", "[ShipmentType] IN ('Letter', 'Parcel')");
                    table.CheckConstraint("CK_Shipments_WeightKg", "[WeightKg] > 0 AND [WeightKg] <= 30");
                    table.ForeignKey(
                        name: "FK_Shipments_Branches_OriginBranchCode",
                        column: x => x.OriginBranchCode,
                        principalTable: "Branches",
                        principalColumn: "BranchCode",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Shipments_ShippingZones_ZoneCode",
                        column: x => x.ZoneCode,
                        principalTable: "ShippingZones",
                        principalColumn: "ZoneCode",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShippingRates",
                columns: table => new
                {
                    ShippingRateId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShipmentType = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    ServiceLevel = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    ZoneCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    Charge = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MaximumWeightKg = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false, defaultValue: "system"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingRates", x => x.ShippingRateId);
                    table.CheckConstraint("CK_ShippingRates_Charge", "[Charge] > 0");
                    table.CheckConstraint("CK_ShippingRates_MaximumWeightKg", "[MaximumWeightKg] > 0 AND [MaximumWeightKg] <= 30");
                    table.CheckConstraint("CK_ShippingRates_ServiceLevel", "[ServiceLevel] IN ('Standard', 'Express', 'Registered')");
                    table.CheckConstraint("CK_ShippingRates_ShipmentType", "[ShipmentType] IN ('Letter', 'Parcel')");
                    table.ForeignKey(
                        name: "FK_ShippingRates_ShippingZones_ZoneCode",
                        column: x => x.ZoneCode,
                        principalTable: "ShippingZones",
                        principalColumn: "ZoneCode",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IdentityVerifications",
                columns: table => new
                {
                    IdentityVerificationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MoneyOrderId = table.Column<int>(type: "int", nullable: false),
                    DocumentType = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false),
                    MaskedDocumentReference = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    Outcome = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false, defaultValue: "system"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityVerifications", x => x.IdentityVerificationId);
                    table.CheckConstraint("CK_IdentityVerifications_Masked", "[MaskedDocumentReference] LIKE '****%'");
                    table.CheckConstraint("CK_IdentityVerifications_Outcome", "[Outcome] IN ('Verified', 'Failed')");
                    table.ForeignKey(
                        name: "FK_IdentityVerifications_MoneyOrders_MoneyOrderId",
                        column: x => x.MoneyOrderId,
                        principalTable: "MoneyOrders",
                        principalColumn: "MoneyOrderId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BranchStock",
                columns: table => new
                {
                    BranchStockId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    ProductCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false, defaultValue: "system"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchStock", x => x.BranchStockId);
                    table.UniqueConstraint("AK_BranchStock_BranchCode_ProductCode", x => new { x.BranchCode, x.ProductCode });
                    table.CheckConstraint("CK_BranchStock_Quantity", "[Quantity] >= 0");
                    table.ForeignKey(
                        name: "FK_BranchStock_Branches_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "Branches",
                        principalColumn: "BranchCode",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BranchStock_Products_ProductCode",
                        column: x => x.ProductCode,
                        principalTable: "Products",
                        principalColumn: "ProductCode",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CounterTransactions",
                columns: table => new
                {
                    CounterTransactionId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransactionCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    BranchCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    CounterCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    ClerkStaffCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Status = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    StatusReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AuthorizedByStaffCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false, defaultValue: "system"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CounterTransactions", x => x.CounterTransactionId);
                    table.UniqueConstraint("AK_CounterTransactions_TransactionCode", x => x.TransactionCode);
                    table.CheckConstraint("CK_CounterTransactions_Amounts", "[Subtotal] >= 0 AND [Tax] >= 0");
                    table.CheckConstraint("CK_CounterTransactions_Reversal", "([Status] IN ('Open', 'Paid') AND [AuthorizedByStaffCode] IS NULL AND [StatusReason] IS NULL) OR ([Status] IN ('Voided', 'Refunded') AND [AuthorizedByStaffCode] IS NOT NULL AND [StatusReason] IS NOT NULL)");
                    table.CheckConstraint("CK_CounterTransactions_Status", "[Status] IN ('Open', 'Paid', 'Voided', 'Refunded')");
                    table.ForeignKey(
                        name: "FK_CounterTransactions_Counters_BranchCode_CounterCode",
                        columns: x => new { x.BranchCode, x.CounterCode },
                        principalTable: "Counters",
                        principalColumns: new[] { "BranchCode", "CounterCode" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CounterTransactions_Staff_AuthorizedByStaffCode",
                        column: x => x.AuthorizedByStaffCode,
                        principalTable: "Staff",
                        principalColumn: "StaffCode",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CounterTransactions_Staff_ClerkStaffCode",
                        column: x => x.ClerkStaffCode,
                        principalTable: "Staff",
                        principalColumn: "StaffCode",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockAdjustments",
                columns: table => new
                {
                    StockAdjustmentId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    ProductCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    Delta = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ChangedByStaffCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false, defaultValue: "system"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockAdjustments", x => x.StockAdjustmentId);
                    table.CheckConstraint("CK_StockAdjustments_Delta", "[Delta] <> 0");
                    table.ForeignKey(
                        name: "FK_StockAdjustments_BranchStock_BranchCode_ProductCode",
                        columns: x => new { x.BranchCode, x.ProductCode },
                        principalTable: "BranchStock",
                        principalColumns: new[] { "BranchCode", "ProductCode" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockAdjustments_Staff_ChangedByStaffCode",
                        column: x => x.ChangedByStaffCode,
                        principalTable: "Staff",
                        principalColumn: "StaffCode",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    CounterTransactionId = table.Column<long>(type: "bigint", nullable: false),
                    PaymentMethod = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false, defaultValue: "system"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.CounterTransactionId);
                    table.CheckConstraint("CK_Payments_Amount", "[Amount] >= 0");
                    table.CheckConstraint("CK_Payments_PaymentMethod", "[PaymentMethod] IN ('Cash', 'Card', 'DigitalWallet')");
                    table.ForeignKey(
                        name: "FK_Payments_CounterTransactions_CounterTransactionId",
                        column: x => x.CounterTransactionId,
                        principalTable: "CounterTransactions",
                        principalColumn: "CounterTransactionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransactionLines",
                columns: table => new
                {
                    TransactionLineId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CounterTransactionId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false, defaultValue: "system"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionLines", x => x.TransactionLineId);
                    table.CheckConstraint("CK_TransactionLines_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_TransactionLines_UnitPrice", "[UnitPrice] >= 0");
                    table.ForeignKey(
                        name: "FK_TransactionLines_CounterTransactions_CounterTransactionId",
                        column: x => x.CounterTransactionId,
                        principalTable: "CounterTransactions",
                        principalColumn: "CounterTransactionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TransactionLines_Products_ProductCode",
                        column: x => x.ProductCode,
                        principalTable: "Products",
                        principalColumn: "ProductCode",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ProductCategories",
                columns: new[] { "ProductCategoryId", "CategoryCode", "CreatedAtUtc", "CreatedBy", "ModifiedAtUtc", "Name" },
                values: new object[] { 1, "GENERAL", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "migration", null, "General" });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "RoleId", "CreatedAtUtc", "CreatedBy", "ModifiedAtUtc", "Name" },
                values: new object[,]
                {
                    { (byte)0, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "migration", null, "CounterClerk" },
                    { (byte)1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "migration", null, "BranchManager" },
                    { (byte)2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "migration", null, "Auditor" },
                    { (byte)3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "migration", null, "Administrator" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_BranchStock_ProductCode",
                table: "BranchStock",
                column: "ProductCode");

            migrationBuilder.CreateIndex(
                name: "IX_CounterTransactions_AuthorizedByStaffCode",
                table: "CounterTransactions",
                column: "AuthorizedByStaffCode");

            migrationBuilder.CreateIndex(
                name: "IX_CounterTransactions_BranchCode_CounterCode",
                table: "CounterTransactions",
                columns: new[] { "BranchCode", "CounterCode" });

            migrationBuilder.CreateIndex(
                name: "IX_CounterTransactions_ClerkStaffCode",
                table: "CounterTransactions",
                column: "ClerkStaffCode");

            migrationBuilder.CreateIndex(
                name: "IX_IdentityVerifications_MoneyOrderId",
                table: "IdentityVerifications",
                column: "MoneyOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MoneyOrders_BranchCode",
                table: "MoneyOrders",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryCode",
                table: "Products",
                column: "CategoryCode");

            migrationBuilder.CreateIndex(
                name: "UX_Products_Sku",
                table: "Products",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_OriginBranchCode",
                table: "Shipments",
                column: "OriginBranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_ZoneCode",
                table: "Shipments",
                column: "ZoneCode");

            migrationBuilder.CreateIndex(
                name: "UX_ShippingRates_Band",
                table: "ShippingRates",
                columns: new[] { "ZoneCode", "ShipmentType", "ServiceLevel", "MaximumWeightKg" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Staff_BranchCode",
                table: "Staff",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_Staff_RoleId",
                table: "Staff",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "UX_Staff_Email",
                table: "Staff",
                column: "Email",
                unique: true,
                filter: "[Email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_BranchCode_ProductCode",
                table: "StockAdjustments",
                columns: new[] { "BranchCode", "ProductCode" });

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_ChangedByStaffCode",
                table: "StockAdjustments",
                column: "ChangedByStaffCode");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLines_CounterTransactionId",
                table: "TransactionLines",
                column: "CounterTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLines_ProductCode",
                table: "TransactionLines",
                column: "ProductCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "IdentityVerifications");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "Shipments");

            migrationBuilder.DropTable(
                name: "ShippingRates");

            migrationBuilder.DropTable(
                name: "StockAdjustments");

            migrationBuilder.DropTable(
                name: "TransactionLines");

            migrationBuilder.DropTable(
                name: "MoneyOrders");

            migrationBuilder.DropTable(
                name: "ShippingZones");

            migrationBuilder.DropTable(
                name: "BranchStock");

            migrationBuilder.DropTable(
                name: "CounterTransactions");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Counters");

            migrationBuilder.DropTable(
                name: "Staff");

            migrationBuilder.DropTable(
                name: "ProductCategories");

            migrationBuilder.DropTable(
                name: "Branches");

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
