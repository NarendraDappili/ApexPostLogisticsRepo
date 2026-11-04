using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexPost.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Completes the audit trail beside AuditEntries (one row per MVC request): DataChanges holds the old and new
    /// values of every changed row, SecurityEvents holds sign-ins, sign-outs and refused requests. Neither has a
    /// foreign key, so the history outlives the rows it describes. The indexes serve the audit log screen's filters:
    /// a branch and a period, every branch for a period, and one request's trace by its correlation ID.
    /// </summary>
    public partial class AddAuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataChanges",
                columns: table => new
                {
                    DataChangeId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    StaffCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: true),
                    BranchCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: true),
                    TableName = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    RecordKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Operation = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    ChangesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false, defaultValue: "system"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataChanges", x => x.DataChangeId);
                    table.CheckConstraint("CK_DataChanges_ChangesJson", "ISJSON([ChangesJson]) = 1");
                    table.CheckConstraint("CK_DataChanges_Operation", "[Operation] IN ('Insert', 'Update', 'Delete')");
                });

            migrationBuilder.CreateTable(
                name: "SecurityEvents",
                columns: table => new
                {
                    SecurityEventId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    EventType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    StaffCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: true),
                    Role = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    BranchCode = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: true),
                    HttpMethod = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    RemoteIpAddress = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CorrelationId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false, defaultValue: "system"),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityEvents", x => x.SecurityEventId);
                    table.CheckConstraint("CK_SecurityEvents_EventType", "[EventType] IN ('SignInSucceeded', 'SignInFailed', 'SignedOut', 'AccessDenied', 'RateLimited', 'SessionRejected')");
                    table.CheckConstraint("CK_SecurityEvents_StatusCode", "[StatusCode] BETWEEN 100 AND 599");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_BranchCode_OccurredAtUtc",
                table: "AuditEntries",
                columns: new[] { "BranchCode", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_CorrelationId",
                table: "AuditEntries",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredAtUtc",
                table: "AuditEntries",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DataChanges_BranchCode_OccurredAtUtc",
                table: "DataChanges",
                columns: new[] { "BranchCode", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DataChanges_CorrelationId",
                table: "DataChanges",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_DataChanges_OccurredAtUtc",
                table: "DataChanges",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DataChanges_TableName_RecordKey",
                table: "DataChanges",
                columns: new[] { "TableName", "RecordKey" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvents_CorrelationId",
                table: "SecurityEvents",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvents_EventType_OccurredAtUtc",
                table: "SecurityEvents",
                columns: new[] { "EventType", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvents_OccurredAtUtc",
                table: "SecurityEvents",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvents_StaffCode_OccurredAtUtc",
                table: "SecurityEvents",
                columns: new[] { "StaffCode", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataChanges");

            migrationBuilder.DropTable(
                name: "SecurityEvents");

            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_BranchCode_OccurredAtUtc",
                table: "AuditEntries");

            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_CorrelationId",
                table: "AuditEntries");

            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_OccurredAtUtc",
                table: "AuditEntries");
        }
    }
}
