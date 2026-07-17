using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddErasurePipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComplianceAuditLogs",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    ErasureRequestId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubjectIdHash = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestedBy = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    TablesAffected = table.Column<string>(type: "TEXT", nullable: false),
                    RowsAffected = table.Column<int>(type: "INTEGER", nullable: false),
                    KeysShredded = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceAuditLogs", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "ErasureRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectKeyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubjectIdHash = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    RequestedBy = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CryptoShredApplied = table.Column<bool>(type: "INTEGER", nullable: false),
                    AnonymizationApplied = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExportLocation = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2048,
                        nullable: true
                    ),
                    ExportFormat = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: true
                    ),
                    RowsAffected = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", nullable: true),
                    ReportJson = table.Column<string>(type: "TEXT", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErasureRequests", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceAuditLogs_ErasureRequestId",
                table: "ComplianceAuditLogs",
                column: "ErasureRequestId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceAuditLogs_SubjectIdHash",
                table: "ComplianceAuditLogs",
                column: "SubjectIdHash"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ErasureRequests_Status",
                table: "ErasureRequests",
                column: "Status"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ErasureRequests_SubjectUserId_RequestedAt",
                table: "ErasureRequests",
                columns: new[] { "SubjectUserId", "RequestedAt" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ComplianceAuditLogs");

            migrationBuilder.DropTable(name: "ErasureRequests");
        }
    }
}
